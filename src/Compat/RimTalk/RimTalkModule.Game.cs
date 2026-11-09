using HarmonyLib;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    internal sealed class RimTalkModule : CompatModule
    {
        public override string TargetPackageId => "cj.rimtalk";

        // The family spans the core mod and its satellite addons; any one present activates
        // the module, and each registration keeps its own package gate.
        public override bool ShouldActivate =>
            ModsConfig.IsActive("cj.rimtalk")
            || ModsConfig.IsActive("nitoritech.rimtalk.tts")
            || ModsConfig.IsActive("cj.rimtalk.literature")
            || ModsConfig.IsActive("cj.rimtalk.expandmemory")
            || ModsConfig.IsActive("RP.RimTalk.PersonaDirector")
            || ModsConfig.IsActive("ruaji.rimtalkpromptenhance")
            || ModsConfig.IsActive("rimtalk.quests")
            || ModsConfig.IsActive("sanguo.rimtalk.expandactions");

        public override void Activate(Harmony harmony)
        {
            RimTalkNarrativeCompat.Register(harmony);
            RimTalkChatDialogCompat.Register();
            RimTalkPersonaDialogCompat.Register();
            RimTalkDebugWindowCompat.Register();
            RimTalkGenericWindowsCompat.Register();
            RimTalkTtsCompat.Register();
            RimTalkMemoryCompat.Register();
            PersonaDirectorCompat.Register();
            PromptEnhanceCompat.Register();
            RimTalkLiteratureCompat.Register();
            RimTalkLiteratureCompat.ApplyPatches(harmony);
            RimTalkQuestsCompat.Register();
            ExpandActionsNarrativeCompat.Register(harmony);
        }
    }
}
