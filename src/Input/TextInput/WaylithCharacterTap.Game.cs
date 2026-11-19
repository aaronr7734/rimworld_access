using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Restores the per-keypress character stream on RimWorld 1.7's native windowing layer
    /// (Waylith.Windowing). That layer raises TextInput only while a window text session is open,
    /// and the game opens one only for a focused IMGUI text field, so the shell's char sinks
    /// (typeahead, its own edit sessions) would otherwise never see a character. Outside a session
    /// this taps every KeyDown's layout-translated text into the same
    /// <c>Keyboard.onTextInput</c> queue the session path feeds, which ImguiDispatch replays as
    /// <c>keyCode == None</c> character events ahead of the key's own event.
    ///
    /// The observer type is emitted at runtime: <c>IWindowEventObserver</c> exists only on 1.7, and
    /// its <c>KeyEvent</c> payload is a ref struct that reflection cannot read. Absent the 1.7
    /// surface (1.6), <see cref="Install"/> does nothing.
    /// </summary>
    public static class WaylithCharacterTap
    {
        private static Action<char> queueTextInput;

        // Holds the platform's observer registration; disposing it would detach the tap.
        private static IDisposable registration;

        private static bool platformResolved;

        /// <summary>The 1.7 Waylith.Windowing assembly, or null on 1.6.</summary>
        internal static Assembly WindowingAssembly { get; private set; }

        /// <summary>The engine's <c>IPlatform</c> (EngineApplication.Instance.WindowSystem), or null on 1.6.</summary>
        internal static object Platform { get; private set; }

        /// <summary>
        /// Resolves <see cref="WindowingAssembly"/> and <see cref="Platform"/> once, by exact
        /// assembly name: a missing type must not trigger AccessTools' all-assemblies sweep, which
        /// is every miss on 1.6.
        /// </summary>
        internal static void ResolvePlatform()
        {
            if (platformResolved)
                return;
            platformResolved = true;
            Type engineType = FindAssembly("Waylith")?.GetType("Waylith.EngineApplication");
            if (engineType == null)
                return;
            WindowingAssembly = FindAssembly("Waylith.Windowing");
            object engine = AccessTools.Property(engineType, "Instance")?.GetValue(null);
            Platform = engine == null ? null : AccessTools.Property(engineType, "WindowSystem")?.GetValue(engine);
        }

        private static Assembly FindAssembly(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == name)
                    return assembly;
            }
            return null;
        }

        /// <summary>Must run on the application (main) thread; the platform enforces it.</summary>
        internal static void Install()
        {
            if (registration != null)
                return;

            if (FindAssembly("Waylith") == null)
                return;

            try
            {
                ResolvePlatform();
                Assembly windowing = WindowingAssembly;
                object platform = Platform;
                Type observerInterface = windowing?.GetType("Waylith.Windowing.IWindowEventObserver");
                MethodInfo queue = AccessTools.Method(
                    FindAssembly("UnityStubs")?.GetType("UnityEngine.InputSystem.InputSystem"), "QueueTextInput", new[] { typeof(char) });
                MethodInfo addObserver = platform == null || observerInterface == null
                    ? null : platform.GetType().GetMethod("AddEventObserver", new[] { observerInterface });
                if (queue == null || addObserver == null)
                {
                    ModLogger.Warning("Waylith character tap: windowing surface not found; typed characters will not reach the shell.");
                    return;
                }

                queueTextInput = (Action<char>)Delegate.CreateDelegate(typeof(Action<char>), queue);
                object observer = Activator.CreateInstance(EmitObserver(observerInterface, windowing));
                registration = (IDisposable)addObserver.Invoke(platform, new[] { observer });
            }
            catch (Exception ex)
            {
                ModLogger.Error("Waylith character tap failed to install: " + ex);
            }
        }

        private static bool editSessionOpen;
        private static IMECompositionMode modeBeforeEditSession;
        private static LoadedLanguage imeCheckedLanguage;
        private static bool languageUsesIme;

        /// <summary>
        /// Every OnGUI pass: holds the platform text session open while a shell text-edit sink is
        /// live, so the OS keyboard treats it as a text field (dead keys compose, Option+E then E
        /// types "é"). Typeahead stays session-free, since a session turns Option letters into
        /// dead-key composition and would eat Alt chords such as Alt+I. CJK players keep
        /// <see cref="ImeInputHost"/>, which owns the composition mode itself.
        /// </summary>
        internal static void PumpEditSession(bool editSinkActive)
        {
            if (editSinkActive && registration != null && imeCheckedLanguage != LanguageDatabase.activeLanguage)
            {
                imeCheckedLanguage = LanguageDatabase.activeLanguage;
                languageUsesIme = ImeInputHost.LanguageUsesIme();
            }
            bool wanted = editSinkActive && registration != null && !languageUsesIme;
            if (wanted == editSessionOpen)
                return;

            editSessionOpen = wanted;
            if (wanted)
            {
                modeBeforeEditSession = Input.imeCompositionMode;
                Input.imeCompositionMode = IMECompositionMode.On;
            }
            else
            {
                Input.imeCompositionMode = modeBeforeEditSession;
            }
        }

        /// <summary>
        /// Called from the emitted observer with a non-composing KeyDown's text while no window
        /// text session is active. Same printable rule as the platform's own text delivery: no
        /// control characters, and no macOS function-key private-use codes.
        /// </summary>
        public static void OnKeyText(string text)
        {
            foreach (char c in text)
            {
                if (c < ' ' || c == '\u007f' || (c >= '' && c <= ''))
                    continue;
                queueTextInput(c);
            }
        }

        /// <summary>
        /// Emits <c>OnEvent(IWindow window, in EventRecord e)</c> as:
        /// <code>
        /// if (e.Kind != WindowEventKind.KeyDown || window.TextInput.IsActive) return;
        /// KeyEvent key = e.Key;
        /// if (key.IsComposing) return;
        /// WaylithCharacterTap.OnKeyText(key.Text.ToString());
        /// </code>
        /// </summary>
        private static Type EmitObserver(Type observerInterface, Assembly windowingAssembly)
        {
            MethodInfo onEvent = observerInterface.GetMethod("OnEvent");
            ParameterInfo[] parameters = onEvent.GetParameters();
            Type windowType = parameters[0].ParameterType;
            Type recordByRef = parameters[1].ParameterType;
            Type recordType = recordByRef.GetElementType();
            Type kindType = windowingAssembly.GetType("Waylith.Windowing.WindowEventKind", true);
            Type textInputType = windowingAssembly.GetType("Waylith.Windowing.ITextInput", true);
            Type keyEventType = windowingAssembly.GetType("Waylith.Windowing.KeyEvent", true);
            Type spanType = Type.GetType("System.ReadOnlySpan`1").MakeGenericType(typeof(char));

            MethodInfo getKind = recordType.GetProperty("Kind").GetGetMethod();
            MethodInfo getKey = recordType.GetProperty("Key").GetGetMethod();
            MethodInfo getTextInput = windowType.GetProperty("TextInput").GetGetMethod();
            MethodInfo getIsActive = textInputType.GetProperty("IsActive").GetGetMethod();
            MethodInfo getIsComposing = keyEventType.GetProperty("IsComposing").GetGetMethod();
            MethodInfo getText = keyEventType.GetProperty("Text").GetGetMethod();
            MethodInfo spanToString = spanType.GetMethod("ToString", Type.EmptyTypes);
            MethodInfo callback = typeof(WaylithCharacterTap).GetMethod(nameof(OnKeyText));
            int keyDown = Convert.ToInt32(Enum.Parse(kindType, "KeyDown"));

            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("RimWorldAccess.WaylithCharacterTap"), AssemblyBuilderAccess.Run);
            TypeBuilder type = assembly.DefineDynamicModule("RimWorldAccess.WaylithCharacterTap")
                .DefineType("WaylithCharacterObserver", TypeAttributes.Public | TypeAttributes.Sealed, typeof(object), new[] { observerInterface });

            // The `in` parameter's modreq(InAttribute) is part of the interface signature.
            MethodBuilder method = type.DefineMethod(onEvent.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                CallingConventions.HasThis, typeof(void), null, null,
                new[] { windowType, recordByRef },
                new[] { parameters[0].GetRequiredCustomModifiers(), parameters[1].GetRequiredCustomModifiers() },
                new[] { parameters[0].GetOptionalCustomModifiers(), parameters[1].GetOptionalCustomModifiers() });

            ILGenerator il = method.GetILGenerator();
            LocalBuilder key = il.DeclareLocal(keyEventType);
            LocalBuilder text = il.DeclareLocal(spanType);
            Label done = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, getKind);
            il.Emit(OpCodes.Ldc_I4, keyDown);
            il.Emit(OpCodes.Bne_Un, done);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, getTextInput);
            il.Emit(OpCodes.Callvirt, getIsActive);
            il.Emit(OpCodes.Brtrue, done);

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, getKey);
            il.Emit(OpCodes.Stloc, key);
            il.Emit(OpCodes.Ldloca, key);
            il.Emit(OpCodes.Call, getIsComposing);
            il.Emit(OpCodes.Brtrue, done);

            il.Emit(OpCodes.Ldloca, key);
            il.Emit(OpCodes.Call, getText);
            il.Emit(OpCodes.Stloc, text);
            il.Emit(OpCodes.Ldloca, text);
            il.Emit(OpCodes.Call, spanToString);
            il.Emit(OpCodes.Call, callback);

            il.MarkLabel(done);
            il.Emit(OpCodes.Ret);

            type.DefineMethodOverride(method, onEvent);
            return type.CreateType();
        }
    }
}
