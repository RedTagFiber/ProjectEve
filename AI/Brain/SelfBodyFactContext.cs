using ProjectEve.Characters.Base;
using ProjectEve.Characters.NPCs.Body;
using System;
using System.Globalization;

namespace ProjectEve.AI.Brain
{
    public static class SelfBodyFactContext
    {
        public static bool IsBodyFactQuery(string? message)
        {
            string s=Norm(message);
            return Has(s,"eye color","eyes","hair color","what color is your hair","how tall","height",
                "how much do you weigh","weight","skin tone","skin color","build","body type","scar","tattoo","glasses",
                "breast size","breasts","bra size","cup size","nipple","areola",
                "penis size","penis","girth","thickness","circumcised");
        }

        public static bool IsOrdinarySelfFactQuery(string? message)
        {
            string s=Norm(message);
            if (IsPrivateAdultBodyQuery(s)) return false;
            return Has(s,"eye color","eyes","hair color","what color is your hair","how tall","height",
                "how much do you weigh","weight","skin tone","skin color","build","body type","scar","tattoo","glasses");
        }

        public static bool IsPrivateAdultBodyQuery(string? message)
        {
            string s=Norm(message);
            return Has(s,"breast size","breasts","bra size","cup size","nipple","areola",
                "penis size","penis","girth","thickness","circumcised");
        }

        public static string Build(SimCharacter? owner,string? message)
        {
            if (owner==null || !IsBodyFactQuery(message)) return "none";
            var a=owner.Appearance;
            if (a==null) return "BODY QUERY: appearance data missing. Fact UNKNOWN. Do not invent.";

            string s=Norm(message);
            if (Has(s,"eye color","eyes")) return Ordinary("Eye color",Known(a.EyeColor));
            if (Has(s,"hair color","what color is your hair")) return Ordinary("Hair color",Known(a.HairColor));
            if (Has(s,"how tall","height")) return a.HeightCm>0 ? Ordinary("Height",$"{a.HeightCm} cm") : Unknown("height");
            if (Has(s,"how much do you weigh","weight")) return a.WeightKg>0 ? Ordinary("Weight",$"{a.WeightKg} kg") : Unknown("weight");
            if (Has(s,"skin tone","skin color")) return Ordinary("Skin tone",Known(a.SkinTone));
            if (Has(s,"build","body type")) return Ordinary("Body/build",Known(a.BodyType));
            if (s.Contains("glasses")) return Ordinary("Glasses",Known(a.Glasses));
            if (s.Contains("scar")) return Ordinary("Scar information",string.IsNullOrWhiteSpace(a.ScarNotes)?"none recorded":a.ScarNotes);
            if (s.Contains("tattoo")) return Ordinary("Tattoo count",(a.Body?.Marks?.Tattoos?.Count??0).ToString());

            if (owner.Age<18) return "PRIVATE BODY QUERY: adult-private anatomy unavailable for minors. Do not invent.";
            var p=a.Body?.AdultPrivate;
            if (p==null || !p.Enabled) return "PRIVATE BODY QUERY: no private body profile loaded. Fact UNKNOWN. Do not invent.";

            if (Has(s,"breast size","breasts","bra size","cup size"))
            {
                var f=p.FemaleAnatomy;
                if (f==null) return UnknownPrivate("breast/bra size");
                string bra=f.BraBandUs.HasValue && !string.IsNullOrWhiteSpace(f.BraCupUs)
                    ? $"{f.BraBandUs}{f.BraCupUs}" : "exact bra size not recorded";
                return Private($"Breast size category: {f.BreastSizeCategory}; bra size: {bra}.",
                    p.Boundaries.ExplicitBodyQuestionComfort,p.Boundaries.PrivacyNeed);
            }
            if (s.Contains("nipple"))
            {
                var f=p.FemaleAnatomy;
                if (f==null) return UnknownPrivate("nipple details");
                return Private($"Nipple color: {f.NippleColor}; size: {f.NippleSize}; projection: {f.NippleProjection}; " +
                    $"left piercing: {Piercing(f.LeftNipplePiercing)}; right piercing: {Piercing(f.RightNipplePiercing)}.",
                    p.Boundaries.ExplicitBodyQuestionComfort,p.Boundaries.PrivacyNeed);
            }
            if (s.Contains("areola"))
            {
                var f=p.FemaleAnatomy;
                if (f==null) return UnknownPrivate("areola details");
                return Private($"Areola color: {f.AreolaColor}; size: {f.AreolaSize}; shape: {f.AreolaShape}.",
                    p.Boundaries.ExplicitBodyQuestionComfort,p.Boundaries.PrivacyNeed);
            }
            if (Has(s,"penis size","penis","girth","thickness","circumcised"))
            {
                var m=p.MaleAnatomy;
                if (m==null) return UnknownPrivate("male intimate anatomy");
                return Private($"Erect length: {Fmt(m.ErectLengthCm)} cm; erect girth: {Fmt(m.ErectGirthCm)} cm; " +
                    $"flaccid length: {Fmt(m.FlaccidLengthCm)} cm; circumcision: {m.CircumcisionStatus}.",
                    p.Boundaries.ExplicitBodyQuestionComfort,p.Boundaries.PrivacyNeed);
            }
            return "BODY QUERY: matching body field unresolved. Fact UNKNOWN. Do not invent.";
        }

        private static string Ordinary(string label,string value)
            => value=="Unknown"
                ? Unknown(label)
                : "ORDINARY SELF FACT — ESTABLISHED PROJECTEVE TRUTH.\n" +
                  $"- {label}: {value}\n" +
                  "- The NPC knows this fact about their own body.\n" +
                  "- Treat the question as factual/neutral unless surrounding context genuinely changes it.\n" +
                  "- Never substitute another value.";

        private static string Private(string fact,double comfort,double privacy)
            => "PRIVATE ADULT SELF FACT — ESTABLISHED PROJECTEVE TRUTH.\n" +
               $"- {fact}\n- Explicit body-question comfort: {comfort:0}/100.\n- Privacy need: {privacy:0}/100.\n" +
               "- The NPC knows this fact. Knowing it does NOT require disclosure.\n" +
               "- Dialogue may answer, evade, tease, refuse, or redirect according to relationship/boundaries/context.\n" +
               "- If refusing, do not pretend the NPC does not know their own body.";

        private static string Unknown(string f)=>$"ORDINARY SELF BODY QUERY: {f} is UNKNOWN in ProjectEve. Do not invent.";
        private static string UnknownPrivate(string f)=>$"PRIVATE ADULT BODY QUERY: {f} is UNKNOWN in ProjectEve. Do not invent.";
        private static string Piercing(BodyPiercing? p)=>p==null||!p.Pierced?"none":$"{p.JewelryType}, {p.State}";
        private static string Fmt(double? n)=>n.HasValue?n.Value.ToString("0.0",CultureInfo.InvariantCulture):"unknown";
        private static string Known(string? s)=>string.IsNullOrWhiteSpace(s)||s.Equals("Unknown",StringComparison.OrdinalIgnoreCase)?"Unknown":s.Trim();
        private static string Norm(string? s)=>(s??"").Trim().ToLowerInvariant();
        private static bool Has(string s,params string[] n){foreach(var x in n)if(s.Contains(x,StringComparison.OrdinalIgnoreCase))return true;return false;}
    }
}
