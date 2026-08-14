using ProjectEve.Characters.NPCs;
using System;
using System.Text;

namespace ProjectEve.Characters.NPCs.Body
{
    /// <summary>
    /// Selects only the body facts appropriate to the current viewpoint/context.
    /// Do not dump the full HumanBodyProfile into every LLM prompt.
    /// </summary>
    public static class BodyPromptContext
    {
        public static string BuildSelfFacts(NPCAppearance? appearance)
            => appearance?.ToSelfKnowledgeFragment() ?? "";

        public static string BuildCurrentNeed(HumanBodyProfile? body)
            => body == null ? "" : BodyStateService.BuildNeedPrompt(body);

        public static string BuildPublicObservable(NPCAppearance? appearance)
        {
            if (appearance == null) return "";
            return "OBSERVABLE APPEARANCE:\n- " + appearance.ToPromptFragment();
        }

        public static string BuildAdultPrivate(HumanBodyProfile? body)
        {
            if (body == null || body.Identity.AgeYears < 18 || !body.AdultPrivate.Enabled)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("ADULT PRIVATE BODY/PREFERENCE CONTEXT — use only because this scene requires it:");
            sb.AppendLine($"- Privacy need: {body.AdultPrivate.Boundaries.PrivacyNeed:0}/100");
            sb.AppendLine($"- Body-question comfort: {body.AdultPrivate.Boundaries.ExplicitBodyQuestionComfort:0}/100");
            sb.AppendLine($"- Post-sex cleanliness preference: {body.AdultPrivate.Aftercare.PostSexCleanlinessPreference:0}/100");
            sb.AppendLine($"- Fluid tolerance: {body.AdultPrivate.Aftercare.FluidTolerance:0}/100");
            sb.AppendLine($"- Immediate-shower preference: {body.AdultPrivate.Aftercare.ImmediateShowerPreference:0}/100");
            sb.AppendLine($"- Cuddle-before-cleanup preference: {body.AdultPrivate.Aftercare.LikesCuddlingBeforeCleanup:0}/100");
            sb.AppendLine("- These values influence behavior; they do not force disclosure.");
            sb.AppendLine("- Never expose hidden measurements unless the viewpoint character legitimately knows them.");
            return sb.ToString().Trim();
        }
    }
}
