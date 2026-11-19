#if DEBUG
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Verse;

namespace RimWorldAccess.DevBridge
{
    /// <summary>
    /// A localhost-only HTTP server that lets external tooling (e.g. Claude Code via curl)
    /// run C# against the live game for verification and inspection. Debug builds only -
    /// the entire DevBridge module is compiled out of Release, so it can never reach players.
    ///
    /// Endpoints:
    ///   GET  /            - usage help
    ///   GET  /health      - liveness + Roslyn/game state
    ///   POST /eval        - body is C# script text; runs on the main thread, returns result
    ///   GET  /eval?code=  - same, for quick one-liners
    ///
    /// Bound to 127.0.0.1 only, so it accepts loopback connections exclusively.
    /// </summary>
    internal static class DevBridgeServer
    {
        // Override with the RWA_DEVBRIDGE_PORT environment variable if 8787 is taken.
        private const int DefaultPort = 8787;

        private static HttpListener listener;
        private static Thread worker;
        private static bool startAttempted;
        private static int port;

        /// <summary>
        /// Start the server once. Safe to call every frame; only the first call does work.
        /// Called from <see cref="DevBridgeDrainPatch"/> so the bridge is live at the main
        /// menu and in-game without touching the mod's Core init.
        /// </summary>
        internal static void EnsureStarted()
        {
            if (startAttempted) return;
            startAttempted = true;

            port = DefaultPort;
            string portEnv = Environment.GetEnvironmentVariable("RWA_DEVBRIDGE_PORT");
            if (!string.IsNullOrEmpty(portEnv) && int.TryParse(portEnv, out int parsed))
                port = parsed;

            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();

                worker = new Thread(Loop)
                {
                    IsBackground = true,
                    Name = "RWA-DevBridge"
                };
                worker.Start();

                Log.Message($"[RimWorld Access] Dev bridge listening on http://127.0.0.1:{port}/ " +
                            "(DEBUG build only). POST C# to /eval.");
            }
            catch (Exception e)
            {
                Log.Warning($"[RimWorld Access] Dev bridge failed to start on port {port}: {e.Message}");
                listener = null;
            }
        }

        private static void Loop()
        {
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = listener.GetContext();
                }
                catch
                {
                    break; // listener stopped/disposed
                }

                // One worker per request: /inject long-polls for up to 90 seconds, and a
                // serial accept-handle loop would queue every later request (including
                // /health and /eval probes) behind it — smoke-caught when countdown probes
                // during an in-flight sequence returned stale state and a concurrent
                // /inject could never actually reach the already-running rejection.
                // MainThreadDispatcher already serializes the game-state touches.
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try { Handle(ctx); }
                    catch (Exception e)
                    {
                        try { Write(ctx, 500, "ERROR\n" + e); } catch { /* client gone */ }
                    }
                });
            }
        }

        private static long lastRequestTicks;

        internal static bool RecentlyActive
        {
            get { return DateTime.UtcNow.Ticks - Interlocked.Read(ref lastRequestTicks) < TimeSpan.TicksPerMinute; }
        }

        private static void Handle(HttpListenerContext ctx)
        {
            Interlocked.Exchange(ref lastRequestTicks, DateTime.UtcNow.Ticks);
            string path = ctx.Request.Url.AbsolutePath.TrimEnd('/');

            if (path == "/health")
            {
                bool ready = RoslynEvaluator.IsReady;
                string state = MainThreadDispatcher.RunOnMainThread(() =>
                    Current.ProgramState.ToString());
                Write(ctx, 200, $"OK\nroslyn={ready}\nprogramState={state}");
                return;
            }

            if (path == "/eval")
            {
                string code = ReadCode(ctx);
                if (string.IsNullOrWhiteSpace(code))
                {
                    Write(ctx, 400, "ERROR\nNo code provided. POST C# in the body, or GET /eval?code=...");
                    return;
                }

                // Compile + run on the main thread so game state access is safe.
                string result = MainThreadDispatcher.RunOnMainThread(() => RoslynEvaluator.Eval(code));
                Write(ctx, result.StartsWith("ERROR") ? 200 : 200, result);
                return;
            }

            if (path == "/inject")
            {
                HandleInject(ctx);
                return;
            }

            if (path == "/reload")
            {
                // Optional body = explicit DLL path; default is the running assembly's own
                // location, which the deploy step just overwrote. The 120s ceiling covers a
                // first reload's cold reflection sweep.
                string dllPath = ReadCode(ctx);
                string result;
                try
                {
                    result = MainThreadDispatcher.RunOnMainThread(
                        () => HotReloader.Reload(string.IsNullOrWhiteSpace(dllPath) ? null : dllPath.Trim()),
                        timeoutSeconds: 120);
                }
                catch (Exception e)
                {
                    result = "ERROR\n" + e;
                }
                Write(ctx, 200, result);
                return;
            }

            // Root / anything else: usage help.
            Write(ctx, 200,
                "RimWorld Access dev bridge\n" +
                "  GET  /health      - liveness + roslyn/game state\n" +
                "  POST /eval        - body = C# script; returns the last expression's value\n" +
                "  GET  /eval?code=  - same, url-encoded one-liner\n" +
                "  POST /inject      - body (or ?seq=) = a ';'-separated chord/wait/text script,\n" +
                "                      run one step per real frame; optional ?wait=N overrides the\n" +
                "                      default 30-frame gap between steps; returns per-step results\n" +
                "                      plus everything spoken during the run. On 1.7 steps are\n" +
                "                      real key presses through the engine's input injector\n" +
                "                      (works in the background); ?synthetic=1 forces the old\n" +
                "                      synthetic events\n" +
                "  POST /reload      - hot-swap changed method bodies from the freshly deployed\n" +
                "                      DLL (optional body = explicit DLL path); reports swapped\n" +
                "                      methods and anything that needs a restart instead\n\n" +
                "Example:\n" +
                "  curl -s -X POST --data-binary 'return \"ViewEntityCodex\".Translate().ToString();' " +
                $"http://127.0.0.1:{port}/eval\n" +
                "  curl -s -X POST --data-binary 'F12; DownArrow; text:colonist' " +
                $"http://127.0.0.1:{port}/inject");
        }

        private static string ReadCode(HttpListenerContext ctx)
        {
            // Prefer the request body (handles multiline scripts cleanly).
            if (ctx.Request.HasEntityBody)
            {
                using (var reader = new StreamReader(ctx.Request.InputStream,
                           ctx.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(body))
                        return body;
                }
            }
            // Fall back to the ?code= query parameter.
            return ctx.Request.QueryString["code"];
        }

        /// <summary>
        /// Arms a multi-frame injection sequence and blocks this HTTP request (NOT the main thread —
        /// every touch of the sequence state below goes through
        /// <see cref="MainThreadDispatcher.RunOnMainThread{T}"/>, same as /eval) until it finishes or a
        /// 90-second ceiling is hit.
        /// </summary>
        private static void HandleInject(HttpListenerContext ctx)
        {
            string script = ReadCode(ctx);
            if (string.IsNullOrWhiteSpace(script))
            {
                script = ctx.Request.QueryString["seq"];
            }
            if (string.IsNullOrWhiteSpace(script))
            {
                Write(ctx, 400, "ERROR\nNo script provided. POST a sequence body, or GET /inject?seq=...");
                return;
            }

            int wait = 30;
            string waitParam = ctx.Request.QueryString["wait"];
            if (!string.IsNullOrEmpty(waitParam))
            {
                int.TryParse(waitParam, out wait);
            }

            bool real = ctx.Request.QueryString["synthetic"] != "1";
            string startError = MainThreadDispatcher.RunOnMainThread(
                () => RimWorldAccess.Shell.ShellDev.StartSequence(script, wait, real));
            if (startError != null)
            {
                Write(ctx, 200, startError);
                return;
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                bool running = MainThreadDispatcher.RunOnMainThread(() => RimWorldAccess.Shell.ShellDev.SequenceRunning);
                if (!running)
                {
                    break;
                }
                Thread.Sleep(100);
            }

            string result = MainThreadDispatcher.RunOnMainThread(() => RimWorldAccess.Shell.ShellDev.SequenceResult);
            Write(ctx, 200, result ?? "ERROR: sequence timed out after 90s");
        }

        private static void Write(HttpListenerContext ctx, int status, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
    }
}
#endif
