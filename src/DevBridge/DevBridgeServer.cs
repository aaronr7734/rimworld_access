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

                try { Handle(ctx); }
                catch (Exception e)
                {
                    try { Write(ctx, 500, "ERROR\n" + e); } catch { /* client gone */ }
                }
            }
        }

        private static void Handle(HttpListenerContext ctx)
        {
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

            // Root / anything else: usage help.
            Write(ctx, 200,
                "RimWorld Access dev bridge\n" +
                "  GET  /health      - liveness + roslyn/game state\n" +
                "  POST /eval        - body = C# script; returns the last expression's value\n" +
                "  GET  /eval?code=  - same, url-encoded one-liner\n\n" +
                "Example:\n" +
                "  curl -s -X POST --data-binary 'return \"ViewEntityCodex\".Translate().ToString();' " +
                $"http://127.0.0.1:{port}/eval");
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
