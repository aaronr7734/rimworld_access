using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Maps a hub section to the editor it opens. Shared by the Custom-creation hub
    /// (IdeoBuilderHubState) and the in-game reform dialog's free-edit stage
    /// (IdeoReformState) so the "Enter on a section opens its editor" dispatch lives in
    /// one place and operates on whichever Ideo the caller passes.
    /// </summary>
    public static class IdeoBuilderSectionActions
    {
        public static void Activate(Ideo ideo, IdeoBuilderHelper.SectionKind kind)
        {
            if (ideo == null) return;

            switch (kind)
            {
                case IdeoBuilderHelper.SectionKind.StructureMeme:
                    Find.WindowStack.Add(new Dialog_ChooseMemes(ideo, MemeCategory.Structure, initialSelection: false));
                    break;
                case IdeoBuilderHelper.SectionKind.NormalMemes:
                    Find.WindowStack.Add(new Dialog_ChooseMemes(ideo, MemeCategory.Normal, initialSelection: false));
                    break;
                case IdeoBuilderHelper.SectionKind.Precepts:
                    IdeoPreceptSelectionState.Open(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Roles:
                case IdeoBuilderHelper.SectionKind.Rituals:
                case IdeoBuilderHelper.SectionKind.Buildings:
                case IdeoBuilderHelper.SectionKind.Relics:
                case IdeoBuilderHelper.SectionKind.Weapons:
                case IdeoBuilderHelper.SectionKind.VeneratedAnimals:
                case IdeoBuilderHelper.SectionKind.PreferredXenotypes:
                case IdeoBuilderHelper.SectionKind.Apparel:
                    IdeoTypedPreceptState.Open(ideo, kind);
                    break;
                case IdeoBuilderHelper.SectionKind.Name:
                    IdeoSymbolEditState.EditName(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Adjective:
                    IdeoSymbolEditState.EditAdjective(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.MemberName:
                    IdeoSymbolEditState.EditMemberName(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.WorshipRoom:
                    IdeoSymbolEditState.OpenWorshipRoomMenu(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Description:
                    IdeoSymbolEditState.OpenDescriptionMenu(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Culture:
                    IdeoSymbolEditState.OpenCulturePicker(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Styles:
                    IdeoSymbolEditState.OpenStylePicker(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Icon:
                    IdeoSymbolEditState.OpenIconPicker(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Color:
                    IdeoSymbolEditState.OpenColorPicker(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Deities:
                    IdeoDeityListState.Open(ideo);
                    break;
                case IdeoBuilderHelper.SectionKind.Appearance:
                    IdeoAppearanceEditState.Open(ideo);
                    break;
                default:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
            }
        }
    }
}
