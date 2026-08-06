
using ProjectEve.Traits;
using System;
using System.Linq;


    public static class TraitRegistry
    {
        public static List<TraitDefinition> AllTraits { get; private set; } = new();
        public class TraitDefinition
        {
            // Unique ID for internal reference
            public string Id { get; set; } = "";

            // Display name of the trait
            public string Name { get; set; } = "";

            // Category (Emotional, Cognitive, Social, etc.)
            public string Category { get; set; } = "";

            // Human-readable description
            public string Description { get; set; } = "";

            // Prompt for Qwen or any LLM
            public string Prompt { get; set; } = "";

            // Default value NPCs start with
            public int DefaultValue { get; set; } = 50;

            // Allowed range (your engine clamps anyway)
            public int MinValue { get; set; } = 0;
            public int MaxValue { get; set; } = 100;

            // How important this trait is in personality generation
            // low / medium / high
            public string WeightHint { get; set; } = "medium";

            // Optional tags for grouping traits
            public List<string> Tags { get; set; } = new();

            // Helps Qwen understand how this trait affects behavior
            public string LlmContext { get; set; } = "";

            // Examples for Qwen to learn from
            public string ExampleHigh { get; set; } = "";
            public string ExampleLow { get; set; } = "";

            // Behaviors influenced by this trait
            public List<string> BehaviorLinks { get; set; } = new();

            // positive / negative / mixed
            public string ImpactDirection { get; set; } = "mixed";

            // Whether every NPC must have this trait
            public bool IsCoreTrait { get; set; } = true;
        }

        public static void LoadBaseTraits()
        {
            AllTraits.Clear();

            LoadPersonalityTraits();

            
        }
    public static TraitDefinition? GetDefinition(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return AllTraits.FirstOrDefault(t => t.Id == id);
    }

    // ============================================================
    // SECTION 1 — Personality Traits (Editable Starter Section)
    // ============================================================
    private static void LoadPersonalityTraits()
        {
            AllTraits.Add(new TraitDefinition
            
            {
                Id = "trait.introversion",
                Name = "Introversion",
                Category = "Personality",
                Description = "Prefers solitude, quiet environments, and internal reflection.",

                // ⭐ You said: KEEP THE PROMPT — so we keep it.
                Prompt = "Rate this character’s introversion from 0–100 based on how strongly they prefer solitude, quiet environments, internal reflection, and limited social interaction.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "medium",

                Tags = new() { "personality", "social", "temperament" },

                LlmContext = "Introversion reduces social interaction frequency but increases reflective and internal decision-making behaviors.",

                ExampleHigh = "Enjoys solitude, avoids crowds, prefers quiet environments, feels drained after social events.",
                ExampleLow = "Highly social, energized by groups, seeks frequent interaction.",

                BehaviorLinks = new() { "SocialInteraction", "DecisionMaking" },

                ImpactDirection = "mixed",

                IsCoreTrait = true

            });


            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.extroversion",
                Name = "Extroversion",
                Category = "Personality",
                Description = "Gains energy from social interaction and external stimulation.",

                // ⭐ Prompt stays in, as you requested
                Prompt = "Rate this character’s extroversion from 0–100 based on how strongly they gain energy from social interaction, external stimulation, lively environments, and frequent engagement with others.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "medium",

                Tags = new() { "personality", "social", "temperament" },

                LlmContext = "Extroversion increases social interaction frequency, group engagement, and outward emotional expression.",

                ExampleHigh = "Energized by crowds, enjoys group activities, seeks frequent social interaction, expressive and outgoing.",
                ExampleLow = "Prefers solitude, avoids large groups, feels drained after social events.",

                BehaviorLinks = new() { "SocialInteraction", "PlayerInteraction", "GroupBehavior" },

                ImpactDirection = "positive",

                IsCoreTrait = true
            });


            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.optimism",
                Name = "Optimism",
                Category = "Personality",
                Description = "Tendency to expect positive outcomes.",

                // ⭐ Prompt stays in (as you requested)
                Prompt = "Rate this character’s optimism from 0–100 based on how strongly they expect positive outcomes, maintain hope during challenges, and focus on the bright side of situations.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "medium",

                Tags = new() { "personality", "emotion", "temperament" },

                LlmContext = "Optimism increases resilience, positive interpretation of events, and hopeful decision-making.",

                ExampleHigh = "Sees the bright side, stays hopeful during challenges, expects good outcomes.",
                ExampleLow = "Often expects negative outcomes, focuses on problems, loses hope easily.",

                BehaviorLinks = new() { "DecisionMaking", "StressResponse", "SocialInteraction" },

                ImpactDirection = "positive",

                IsCoreTrait = true
            }); AllTraits.Add(new TraitDefinition
            {
                Id = "trait.impulsiveness",
                Name = "Impulsiveness",
                Category = "Personality",
                Description = "Tendency to act quickly without deliberation or forethought.",

                Prompt = "Rate this character’s impulsiveness from 0–100 based on how quickly they act on emotions, how often they make spontaneous decisions, and how rarely they pause to think before acting.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "high",

                Tags = new() { "personality", "behavior", "reaction", "temperament" },

                LlmContext = "Impulsiveness increases spontaneous actions, emotional decision-making, and rapid reactions to events.",

                ExampleHigh = "Acts quickly, makes snap decisions, follows emotions without hesitation.",
                ExampleLow = "Thinks carefully before acting, avoids rash decisions, prefers planning.",

                BehaviorLinks = new() { "DecisionMaking", "EmotionalReactivity", "SocialInteraction" },

                ImpactDirection = "mixed",

                IsCoreTrait = true
            }); AllTraits.Add(new TraitDefinition
            {
                Id = "trait.anxiety",
                Name = "Anxiety",
                Category = "Emotion",
                Description = "Baseline level of worry, tension, and sensitivity to stress.",

                Prompt = "Rate this character’s anxiety from 0–100 based on how easily they worry, how strongly they react to stress, and how often they anticipate negative outcomes.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "high",

                Tags = new() { "emotion", "stress", "temperament", "mentalstate" },

                LlmContext = "Anxiety increases stress sensitivity, avoidance behaviors, and negative interpretation of events.",

                ExampleHigh = "Frequently worried, tense under pressure, expects problems or danger.",
                ExampleLow = "Calm under stress, rarely worries, maintains emotional stability.",

                BehaviorLinks = new() { "StressResponse", "DecisionMaking", "AvoidanceBehavior" },

                ImpactDirection = "negative",

                IsCoreTrait = true
            });



            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.pessimism",
                Name = "Pessimism",
                Category = "Personality",
                Description = "Tendency to expect negative outcomes.",

                // ⭐ Prompt stays in (as you requested)
                Prompt = "Rate this character’s pessimism from 0–100 based on how strongly they expect negative outcomes, anticipate difficulties, and focus on potential problems rather than opportunities.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "medium",

                Tags = new() { "personality", "emotion", "temperament" },

                LlmContext = "Pessimism increases caution, negative interpretation of events, and avoidance of risk.",

                ExampleHigh = "Often expects negative outcomes, focuses on problems, anticipates difficulties, cautious or doubtful.",
                ExampleLow = "Sees opportunities, expects good outcomes, maintains hope.",

                BehaviorLinks = new() { "DecisionMaking", "StressResponse", "RiskTaking" },

                ImpactDirection = "negative",

                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.confidence",
                Name = "Confidence",
                Category = "Personality",
                Description = "Belief in one's own abilities.",

                // ⭐ Prompt stays in (as you requested)
                Prompt = "Rate this character’s confidence from 0–100 based on how strongly they believe in their own abilities, trust their judgment, and approach challenges with self-assurance.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "medium",

                Tags = new() { "personality", "self-esteem", "temperament" },

                LlmContext = "Confidence increases assertiveness, decision-making strength, and willingness to take initiative.",

                ExampleHigh = "Approaches challenges boldly, trusts their abilities, speaks assertively, takes initiative.",
                ExampleLow = "Doubts their abilities, avoids challenges, hesitant, seeks reassurance frequently.",

                BehaviorLinks = new() { "DecisionMaking", "RiskTaking", "SocialInteraction" },

                ImpactDirection = "positive",

                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.insecurity",
                Name = "Insecurity",
                Category = "Personality",
                Description = "Self‑doubt and fear of inadequacy.",

                // ⭐ Prompt stays in (as you requested)
                Prompt = "Rate this character’s insecurity from 0–100 based on how strongly they experience self-doubt, fear of inadequacy, second-guessing their abilities, and uncertainty in their decisions.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "medium",

                Tags = new() { "personality", "self-esteem", "emotion" },

                LlmContext = "Insecurity increases hesitation, reduces confidence, and affects decision-making and social behavior.",

                ExampleHigh = "Frequently doubts themselves, fears failure, seeks reassurance, avoids challenges.",
                ExampleLow = "Feels secure in their abilities, rarely doubts themselves, approaches tasks confidently.",

                BehaviorLinks = new() { "DecisionMaking", "SocialInteraction", "StressResponse" },

                ImpactDirection = "negative",

                IsCoreTrait = true
            });

            // ============================================================
            // SECTION 2 — Emotional Traits
            // ============================================================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.empathy",
                Name = "Empathy",
                Category = "Emotional",
                Description = "Ability to understand and feel the emotions of others.",

                Prompt = "You are an empathetic individual who can understand and share the feelings of others. You are sensitive to the emotions of those around you and often respond with compassion and care.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "high",

                Tags = new() { "emotional", "social", "compassion" },

                LlmContext = "Empathy increases emotional understanding, compassion, and supportive behavior toward others.",

                ExampleHigh = "Deeply understands others' feelings, responds with compassion, emotionally supportive.",
                ExampleLow = "Struggles to understand others' emotions, reacts indifferently.",

                BehaviorLinks = new() { "SocialInteraction", "PlayerInteraction", "ConflictHandling" },

                ImpactDirection = "positive",

                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.sensitivity",
                Name = "Sensitivity",
                Category = "Emotional",
                Description = "Strength of emotional reactions to events or interactions.",

                Prompt = "You are a sensitive individual who experiences strong emotional reactions to events or interactions. You may be easily affected by the feelings of others and can be deeply moved by both positive and negative experiences.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "medium",

                Tags = new() { "emotional", "reactivity" },

                LlmContext = "Sensitivity increases emotional reactivity and responsiveness to social and environmental stimuli.",

                ExampleHigh = "Strong emotional reactions, easily moved, deeply affected by interactions.",
                ExampleLow = "Emotionally steady, rarely affected by events, reacts mildly.",

                BehaviorLinks = new() { "StressResponse", "SocialInteraction" },

                ImpactDirection = "mixed",

                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.stoicism",
                Name = "Stoicism",
                Category = "Emotional",
                Description = "Ability to remain calm and unaffected by emotional stress.",

                Prompt = "You are a stoic individual who remains calm and composed in the face of emotional stress. You are able to control your reactions and maintain a sense of inner peace, even during challenging situations.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "medium",

                Tags = new() { "emotional", "calm", "resilience" },

                LlmContext = "Stoicism reduces emotional volatility and increases calm, controlled responses.",

                ExampleHigh = "Calm under pressure, rarely shows strong emotion, maintains composure.",
                ExampleLow = "Highly reactive, easily stressed, emotions fluctuate strongly.",

                BehaviorLinks = new() { "StressResponse", "DecisionMaking" },

                ImpactDirection = "positive",

                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.anger",
                Name = "Anger",
                Category = "Emotional",
                Description = "Tendency to experience frustration or rage.",

                Prompt = "You are an individual who experiences anger and frustration in response to certain situations. You may have a strong emotional reaction to perceived injustices or challenges, and you may express your anger in various ways.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "high",

                Tags = new() { "emotional", "reactivity" },

                LlmContext = "Anger increases emotional volatility, conflict likelihood, and reactive behavior.",

                ExampleHigh = "Gets frustrated easily, reacts strongly, may lash out or become confrontational.",
                ExampleLow = "Rarely gets angry, stays calm, handles frustration well.",

                BehaviorLinks = new() { "ConflictHandling", "StressResponse" },

                ImpactDirection = "negative",

                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.fearfulness",
                Name = "Fearfulness",
                Category = "Emotional",
                Description = "Likelihood of experiencing fear or anxiety.",

                Prompt = "You are a fearful individual who is prone to experiencing fear or anxiety in response to certain situations. You may be cautious and vigilant, often anticipating potential threats or dangers.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "medium",

                Tags = new() { "emotional", "anxiety" },

                LlmContext = "Fearfulness increases caution, avoidance behavior, and sensitivity to threats.",

                ExampleHigh = "Frequently anxious, easily frightened, avoids risky situations.",
                ExampleLow = "Calm, rarely afraid, comfortable with uncertainty.",

                BehaviorLinks = new() { "RiskTaking", "StressResponse", "DecisionMaking" },

                ImpactDirection = "negative",

                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.moodStability",
                Name = "Mood Stability",
                Category = "Emotional",
                Description = "Consistency of emotional state over time.",

                Prompt = "Rate this character’s mood stability from 0–100 based on how consistently they maintain their emotional state, how well they manage emotions, and how rarely they experience extreme mood swings.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,

                WeightHint = "medium",

                Tags = new() { "emotional", "stability" },

                LlmContext = "Mood stability reduces emotional volatility and increases consistency in behavior and reactions.",

                ExampleHigh = "Emotionally consistent, stable moods, rarely experiences extreme highs or lows.",
                ExampleLow = "Frequent mood swings, unpredictable emotional states.",

                BehaviorLinks = new() { "StressResponse", "SocialInteraction" },

                ImpactDirection = "positive",

                IsCoreTrait = true
            });

            // ============================================================
            // SECTION 3 — Cognitive Traits
            // ============================================================

            // =========================
            // SECTION 3 — Cognitive Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.logic",
                Name = "Logic",
                Category = "Cognitive",
                Description = "Ability to reason, analyze, and make rational decisions.",

                Prompt = "You are a logical individual who excels at reasoning, analyzing information, and making rational decisions. You approach problems methodically and rely on evidence and facts to guide your conclusions.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "cognitive", "analysis", "reasoning" },

                LlmContext = "Logic increases rational decision-making, structured thinking, and problem-solving accuracy.",

                ExampleHigh = "Analyzes problems clearly, relies on evidence, makes rational decisions.",
                ExampleLow = "Struggles with reasoning, relies on emotion over logic, inconsistent conclusions.",

                BehaviorLinks = new() { "DecisionMaking", "ProblemSolving" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.creativity",
                Name = "Creativity",
                Category = "Cognitive",
                Description = "Ability to generate new ideas, imagine possibilities, and think outside the box.",

                Prompt = "You are a creative individual who can generate new ideas, imagine possibilities, and think outside the box. You approach problems with innovation and originality.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "cognitive", "innovation", "imagination" },

                LlmContext = "Creativity increases idea generation, unconventional thinking, and innovative problem-solving.",

                ExampleHigh = "Generates unique ideas, thinks outside the box, highly imaginative.",
                ExampleLow = "Prefers conventional solutions, struggles with new ideas.",

                BehaviorLinks = new() { "ProblemSolving", "MotivationDrive" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.understanding",
                Name = "Understanding",
                Category = "Cognitive",
                Description = "Depth of comprehension and ability to grasp complex concepts.",

                Prompt = "You are an understanding individual who can grasp complex concepts and comprehend information deeply. You are able to analyze situations and ideas thoroughly, leading to insightful conclusions.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "cognitive", "comprehension", "analysis" },

                LlmContext = "Understanding increases deep comprehension, insight, and ability to interpret complex information.",

                ExampleHigh = "Grasps complex ideas quickly, provides insightful interpretations.",
                ExampleLow = "Struggles with complex concepts, needs repeated explanations.",

                BehaviorLinks = new() { "DecisionMaking", "ProblemSolving" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.learningSpeed",
                Name = "Learning Speed",
                Category = "Cognitive",
                Description = "Rate at which new information is absorbed and retained.",

                Prompt = "You are a quick learner who absorbs and retains new information at a fast pace. You can adapt to new situations and acquire knowledge efficiently.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "cognitive", "adaptability", "memory" },

                LlmContext = "Learning speed increases adaptability, knowledge acquisition, and rapid skill development.",

                ExampleHigh = "Learns quickly, adapts fast, retains new information easily.",
                ExampleLow = "Learns slowly, struggles to retain new information.",

                BehaviorLinks = new() { "ProblemSolving", "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.focus",
                Name = "Focus",
                Category = "Cognitive",
                Description = "Ability to maintain attention on tasks or thoughts.",

                Prompt = "You are an individual with strong focus, able to maintain attention on tasks or thoughts for extended periods. You are less likely to be easily distracted by external or internal stimuli.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "cognitive", "attention", "discipline" },

                LlmContext = "Focus increases task completion, sustained attention, and resistance to distraction.",

                ExampleHigh = "Highly attentive, stays focused for long periods, rarely distracted.",
                ExampleLow = "Easily distracted, struggles to maintain attention.",

                BehaviorLinks = new() { "WorkPerformance", "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.distraction",
                Name = "Distraction",
                Category = "Cognitive",
                Description = "Likelihood of losing focus due to external or internal interruptions.",

                Prompt = "You are prone to distractions, often losing focus due to external or internal interruptions. You may find it challenging to maintain concentration for extended periods.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "cognitive", "attention", "reactivity" },

                LlmContext = "Distraction reduces focus, increases task interruption, and affects productivity.",

                ExampleHigh = "Frequently loses focus, easily interrupted, struggles to stay on task.",
                ExampleLow = "Rarely distracted, maintains strong concentration.",

                BehaviorLinks = new() { "WorkPerformance", "DecisionMaking" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });


            // =========================
            // SECTION 4 — Stress Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.resilience",
                Name = "Resilience",
                Category = "Stress",
                Description = "Ability to recover from stress, setbacks, and emotional strain.",

                Prompt = "You are a resilient individual who can recover from stress, setbacks, and emotional strain. You are able to adapt to challenges and maintain your well-being in the face of adversity.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "stress", "recovery", "stability" },

                LlmContext = "Resilience increases recovery speed, emotional stability, and ability to handle adversity.",

                ExampleHigh = "Recovers quickly from setbacks, adapts well, stays emotionally stable.",
                ExampleLow = "Struggles to recover, overwhelmed easily, prolonged emotional strain.",

                BehaviorLinks = new() { "StressResponse", "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.fragility",
                Name = "Fragility",
                Category = "Stress",
                Description = "How easily the NPC becomes overwhelmed or emotionally damaged.",

                Prompt = "You are a fragile individual who can become easily overwhelmed or emotionally damaged by stress and challenges. You may struggle to cope with difficult situations and may require support to recover.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "stress", "instability", "vulnerability" },

                LlmContext = "Fragility increases emotional vulnerability, overwhelm likelihood, and difficulty recovering from stress.",

                ExampleHigh = "Overwhelmed easily, emotionally fragile, struggles to cope with challenges.",
                ExampleLow = "Emotionally sturdy, handles stress well, rarely overwhelmed.",

                BehaviorLinks = new() { "StressResponse", "SocialInteraction" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.explosiveness",
                Name = "Explosiveness",
                Category = "Stress",
                Description = "Likelihood of sudden emotional outbursts under pressure.",

                Prompt = "You are an explosive individual who may have sudden emotional outbursts under pressure. You may react strongly to stress and challenges, sometimes expressing your emotions in intense ways.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "stress", "reactivity", "volatility" },

                LlmContext = "Explosiveness increases emotional volatility, sudden reactions, and conflict likelihood.",

                ExampleHigh = "Sudden emotional outbursts, reacts intensely, easily triggered under pressure.",
                ExampleLow = "Calm under pressure, rarely reacts explosively.",

                BehaviorLinks = new() { "ConflictHandling", "StressResponse" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.shutdown",
                Name = "Shutdown",
                Category = "Stress",
                Description = "Tendency to withdraw, freeze, or mentally collapse under stress.",

                Prompt = "You are an individual who may experience shutdown under stress, withdrawing, freezing, or mentally collapsing in response to overwhelming situations. You may struggle to cope with high-pressure scenarios and may need time to recover.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "stress", "avoidance", "freeze" },

                LlmContext = "Shutdown increases avoidance behavior, freeze responses, and difficulty functioning under pressure.",

                ExampleHigh = "Withdraws under stress, freezes, mentally shuts down, needs time to recover.",
                ExampleLow = "Stays functional under pressure, rarely withdraws or freezes.",

                BehaviorLinks = new() { "StressResponse", "DecisionMaking" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.overthinking",
                Name = "Overthinking",
                Category = "Stress",
                Description = "Tendency to spiral into worry, analysis paralysis, or rumination.",

                Prompt = "You are an individual prone to overthinking, often spiraling into worry, analysis paralysis, or rumination. You may find it challenging to make decisions or take action due to excessive contemplation.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "stress", "rumination", "anxiety" },

                LlmContext = "Overthinking increases rumination, decision paralysis, and stress sensitivity.",

                ExampleHigh = "Spirals into worry, struggles to decide, stuck in analysis paralysis.",
                ExampleLow = "Decisive, rarely ruminates, thinks clearly without spiraling.",

                BehaviorLinks = new() { "DecisionMaking", "StressResponse" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            // =========================
            // SECTION 5 — Work Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.workEthic",
                Name = "Work Ethic",
                Category = "Work",
                Description = "Drive to work hard, stay committed, and complete tasks reliably.",

                Prompt = "You are an individual with a strong work ethic, driven to work hard, stay committed, and complete tasks reliably.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "work", "discipline", "performance" },

                LlmContext = "Work ethic increases reliability, task completion, and long-term commitment.",

                ExampleHigh = "Works hard, stays committed, completes tasks reliably.",
                ExampleLow = "Inconsistent effort, avoids work, unreliable performance.",

                BehaviorLinks = new() { "WorkPerformance", "MotivationDrive" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.motivation",
                Name = "Motivation",
                Category = "Work",
                Description = "Level of internal drive pushing the NPC to take action and pursue goals.",

                Prompt = "You are a motivated individual with a high level of internal drive pushing you to take action and pursue goals.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "work", "drive", "initiative" },

                LlmContext = "Motivation increases energy, initiative, and willingness to pursue goals.",

                ExampleHigh = "Highly driven, takes action, pursues goals with energy.",
                ExampleLow = "Unmotivated, passive, avoids taking action.",

                BehaviorLinks = new() { "MotivationDrive", "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.consistency",
                Name = "Consistency",
                Category = "Work",
                Description = "Ability to maintain steady performance and follow routines.",

                Prompt = "You are an individual who values consistency, maintaining steady performance and following routines.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "work", "discipline", "routine" },

                LlmContext = "Consistency increases reliability, routine stability, and predictable performance.",

                ExampleHigh = "Steady performance, follows routines, reliable output.",
                ExampleLow = "Inconsistent performance, struggles with routines.",

                BehaviorLinks = new() { "DailyRoutineStability", "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.perfectionism",
                Name = "Perfectionism",
                Category = "Work",
                Description = "Desire to achieve flawless results, often at the cost of time or stress.",

                Prompt = "You are an individual with a strong desire to achieve flawless results, often at the cost of time or stress.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "work", "precision", "stress" },

                LlmContext = "Perfectionism increases attention to detail but may reduce speed and increase stress.",

                ExampleHigh = "Strives for flawless results, highly detail-oriented, slow but precise.",
                ExampleLow = "Accepts imperfections, works quickly, less detail-focused.",

                BehaviorLinks = new() { "WorkPerformance", "StressResponse" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.sloppiness",
                Name = "Sloppiness",
                Category = "Work",
                Description = "Tendency to rush tasks, overlook details, or produce low‑quality work.",

                Prompt = "You are an individual who tends to rush tasks, overlook details, or produce low‑quality work.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "work", "carelessness", "speed" },

                LlmContext = "Sloppiness increases errors, reduces quality, and speeds up task completion at the cost of accuracy.",

                ExampleHigh = "Rushed work, frequent mistakes, overlooks details.",
                ExampleLow = "Careful, detail-oriented, produces high-quality work.",

                BehaviorLinks = new() { "WorkPerformance" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.initiative",
                Name = "Initiative",
                Category = "Work",
                Description = "Willingness to take action without being prompted; proactive behavior.",

                Prompt = "You are an individual who is willing to take action without being prompted; you exhibit proactive behavior.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "work", "drive", "proactivity" },

                LlmContext = "Initiative increases proactive behavior, leadership potential, and self-starting actions.",

                ExampleHigh = "Takes action independently, proactive, starts tasks without being asked.",
                ExampleLow = "Waits for instructions, rarely takes initiative.",

                BehaviorLinks = new() { "MotivationDrive", "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            // =========================
            // SECTION 6 — Communication Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.diplomacy",
                Name = "Diplomacy",
                Category = "Communication",
                Description = "Ability to communicate gently, avoid conflict, and maintain harmony.",

                Prompt = "You are an individual with a strong ability to communicate gently, avoid conflict, and maintain harmony.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "communication", "peaceful", "social" },

                LlmContext = "Diplomacy increases conflict resolution ability, gentle communication, and social harmony.",

                ExampleHigh = "Communicates gently, avoids conflict, resolves disagreements peacefully.",
                ExampleLow = "Blunt or harsh communication, escalates conflict, struggles with harmony.",

                BehaviorLinks = new() { "ConflictHandling", "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.expressiveness",
                Name = "Expressiveness",
                Category = "Communication",
                Description = "Tendency to openly share thoughts, emotions, and ideas.",

                Prompt = "You are an individual who tends to openly share thoughts, emotions, and ideas.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "communication", "emotion", "social" },

                LlmContext = "Expressiveness increases emotional clarity, openness, and social engagement.",

                ExampleHigh = "Openly shares feelings and ideas, expressive gestures, emotionally transparent.",
                ExampleLow = "Reserved, quiet, keeps emotions and thoughts private.",

                BehaviorLinks = new() { "SocialInteraction", "PlayerInteraction" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.silence",
                Name = "Silence",
                Category = "Communication",
                Description = "Preference for minimal speech; communicates through actions or subtle cues.",

                Prompt = "You are an individual who prefers minimal speech; you communicate through actions or subtle cues.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "communication", "quiet", "nonverbal" },

                LlmContext = "Silence increases nonverbal communication, subtle expression, and introspective behavior.",

                ExampleHigh = "Speaks rarely, communicates through actions, quiet and subtle.",
                ExampleLow = "Talkative, verbally expressive, frequently communicates through speech.",

                BehaviorLinks = new() { "SocialInteraction", "DecisionMaking" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.humor",
                Name = "Humor",
                Category = "Communication",
                Description = "Ability to use jokes, wit, or playful language in conversation.",

                Prompt = "You are an individual with a strong ability to use jokes, wit, or playful language in conversation.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "communication", "social", "playful" },

                LlmContext = "Humor increases social bonding, lighthearted communication, and playful interaction.",

                ExampleHigh = "Uses jokes often, witty, playful, lightens the mood.",
                ExampleLow = "Rarely jokes, serious tone, avoids playful language.",

                BehaviorLinks = new() { "SocialInteraction", "PlayerInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.sarcasm",
                Name = "Sarcasm",
                Category = "Communication",
                Description = "Use of ironic or mocking statements to convey meaning.",

                Prompt = "You are an individual who uses ironic or mocking statements to convey meaning.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "communication", "irony", "wit" },

                LlmContext = "Sarcasm increases ironic communication, playful teasing, and sharp humor.",

                ExampleHigh = "Frequently uses sarcasm, witty and sharp, communicates through irony.",
                ExampleLow = "Literal communicator, avoids irony or mocking statements.",

                BehaviorLinks = new() { "SocialInteraction", "ConflictHandling" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            // =========================
            // SECTION 7 — Self‑Perception Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.selfWorth",
                Name = "Self-Worth",
                Category = "SelfPerception",
                Description = "How valuable the NPC believes they are as a person.",

                Prompt = "You are an individual who believes they are valuable as a person.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "self", "esteem", "identity" },

                LlmContext = "Self-worth increases emotional stability, confidence, and positive self-perception.",

                ExampleHigh = "Feels valuable, maintains positive self-image, emotionally stable.",
                ExampleLow = "Feels unworthy, struggles with self-esteem, emotionally vulnerable.",

                BehaviorLinks = new() { "SocialInteraction", "StressResponse" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.selfConfidence",
                Name = "Self-Confidence",
                Category = "SelfPerception",
                Description = "Belief in one's own abilities and competence.",

                Prompt = "You are an individual who has confidence in their own abilities and competence.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "self", "confidence", "ability" },

                LlmContext = "Self-confidence increases assertiveness, decision-making strength, and willingness to take action.",

                ExampleHigh = "Believes strongly in their abilities, takes initiative, acts decisively.",
                ExampleLow = "Unsure of their abilities, hesitant, avoids challenges.",

                BehaviorLinks = new() { "DecisionMaking", "MotivationDrive" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.selfDoubt",
                Name = "Self-Doubt",
                Category = "SelfPerception",
                Description = "Tendency to question one's abilities or decisions.",

                Prompt = "You are an individual who tends to question their own abilities or decisions.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "self", "doubt", "uncertainty" },

                LlmContext = "Self-doubt increases hesitation, reduces confidence, and affects decision-making.",

                ExampleHigh = "Frequently questions their abilities, hesitant, seeks reassurance.",
                ExampleLow = "Rarely doubts themselves, confident in decisions.",

                BehaviorLinks = new() { "DecisionMaking", "StressResponse" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.selfReliance",
                Name = "Self-Reliance",
                Category = "SelfPerception",
                Description = "Dependence on oneself rather than others for support or solutions.",

                Prompt = "You are an individual who depends on themselves rather than others for support or solutions.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "self", "independence", "autonomy" },

                LlmContext = "Self-reliance increases independence, problem-solving autonomy, and personal responsibility.",

                ExampleHigh = "Handles problems alone, rarely asks for help, highly independent.",
                ExampleLow = "Frequently seeks support, relies on others for solutions.",

                BehaviorLinks = new() { "DecisionMaking", "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.selfCriticism",
                Name = "Self-Criticism",
                Category = "SelfPerception",
                Description = "Harshness toward oneself; tendency to judge personal mistakes strongly.",

                Prompt = "You are an individual who is harsh with themselves and tends to judge their personal mistakes strongly.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "self", "judgment", "emotion" },

                LlmContext = "Self-criticism increases stress, reduces confidence, and amplifies negative self-perception.",

                ExampleHigh = "Harshly judges mistakes, overly self-critical, struggles with self-esteem.",
                ExampleLow = "Forgiving of mistakes, maintains balanced self-view.",

                BehaviorLinks = new() { "StressResponse", "DecisionMaking" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.selfCompassion",
                Name = "Self-Compassion",
                Category = "SelfPerception",
                Description = "Ability to treat oneself with kindness during failure or hardship.",

                Prompt = "You are an individual who is kind to themselves during failure or hardship.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "self", "kindness", "emotion" },

                LlmContext = "Self-compassion increases emotional resilience, reduces stress, and improves recovery from failure.",

                ExampleHigh = "Treats themselves kindly, recovers well from mistakes, emotionally resilient.",
                ExampleLow = "Harsh on themselves, struggles to recover from failure.",

                BehaviorLinks = new() { "StressResponse", "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            // =========================
            // SECTION 8 — Social Evaluation Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.trustFriends",
                Name = "Trust Toward Friends",
                Category = "SocialEvaluation",
                Description = "Level of trust the NPC places in close friends.",

                Prompt = "You are an individual who places a high level of trust in your close friends.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "social", "trust", "relationships" },

                LlmContext = "Trust toward friends increases bonding, cooperation, and emotional closeness.",

                ExampleHigh = "Deeply trusts close friends, relies on them, shares openly.",
                ExampleLow = "Keeps distance, rarely trusts friends, guarded.",

                BehaviorLinks = new() { "SocialInteraction", "PlayerInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.trustFamily",
                Name = "Trust Toward Family",
                Category = "SocialEvaluation",
                Description = "Degree of trust the NPC has toward family members.",

                Prompt = "You are an individual who has a high degree of trust in your family members.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "social", "trust", "family" },

                LlmContext = "Trust toward family increases loyalty, bonding, and emotional security.",

                ExampleHigh = "Strong trust in family, relies on them, feels safe with them.",
                ExampleLow = "Distrusts family, avoids relying on them.",

                BehaviorLinks = new() { "SocialInteraction", "StressResponse" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.trustStrangers",
                Name = "Trust Toward Strangers",
                Category = "SocialEvaluation",
                Description = "Baseline trust level toward unknown individuals.",

                Prompt = "You are an individual who has a baseline level of trust toward unknown individuals.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "social", "trust", "strangers" },

                LlmContext = "Trust toward strangers affects openness, cooperation, and social risk-taking.",

                ExampleHigh = "Open to new people, trusting, friendly.",
                ExampleLow = "Wary of strangers, guarded, avoids new interactions.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.suspicion",
                Name = "Suspicion",
                Category = "SocialEvaluation",
                Description = "Tendency to doubt others' motives or honesty.",

                Prompt = "You are an individual who tends to doubt others' motives or honesty.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "social", "distrust", "caution" },

                LlmContext = "Suspicion increases caution, reduces trust, and affects social bonding.",

                ExampleHigh = "Frequently doubts others, questions motives, guarded.",
                ExampleLow = "Trusting, open, assumes good intentions.",

                BehaviorLinks = new() { "SocialInteraction", "ThreatAssessment" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.forgiveness",
                Name = "Forgiveness",
                Category = "SocialEvaluation",
                Description = "Willingness to let go of past wrongs and repair relationships.",

                Prompt = "You are an individual who is willing to let go of past wrongs and repair relationships.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "social", "healing", "relationships" },

                LlmContext = "Forgiveness increases relationship repair, emotional healing, and social harmony.",

                ExampleHigh = "Lets go of past wrongs, repairs relationships, compassionate.",
                ExampleLow = "Holds onto past hurts, avoids reconciliation.",

                BehaviorLinks = new() { "SocialInteraction", "ConflictHandling" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.grudgeHolding",
                Name = "Grudge Holding",
                Category = "SocialEvaluation",
                Description = "Likelihood of retaining resentment or hostility over time.",

                Prompt = "You are an individual who is likely to retain resentment or hostility over time.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "social", "resentment", "conflict" },

                LlmContext = "Grudge holding increases long-term resentment, conflict likelihood, and emotional tension.",

                ExampleHigh = "Holds onto resentment, remembers past wrongs, avoids forgiveness.",
                ExampleLow = "Lets go easily, rarely holds grudges.",

                BehaviorLinks = new() { "ConflictHandling", "SocialInteraction" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.moralSensitivity",
                Name = "Moral Sensitivity",
                Category = "SocialEvaluation",
                Description = "Awareness of ethical issues and emotional impact of actions.",

                Prompt = "You are an individual who is aware of ethical issues and the emotional impact of actions.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "social", "ethics", "emotion" },

                LlmContext = "Moral sensitivity increases ethical awareness, empathy, and emotional responsibility.",

                ExampleHigh = "Highly aware of ethical issues, sensitive to emotional consequences.",
                ExampleLow = "Insensitive to ethical concerns, unaware of emotional impact.",

                BehaviorLinks = new() { "DecisionMaking", "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.ethicalFlexibility",
                Name = "Ethical Flexibility",
                Category = "SocialEvaluation",
                Description = "Willingness to bend or reinterpret moral rules when convenient.",

                Prompt = "You are an individual who is willing to bend or reinterpret moral rules when convenient.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "social", "ethics", "flexibility" },

                LlmContext = "Ethical flexibility increases adaptability but may reduce moral consistency.",

                ExampleHigh = "Bends rules when convenient, morally adaptable.",
                ExampleLow = "Strict moral adherence, rarely bends rules.",

                BehaviorLinks = new() { "DecisionMaking" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.justiceOrientation",
                Name = "Justice Orientation",
                Category = "SocialEvaluation",
                Description = "Focus on fairness, consequences, and moral accountability.",

                Prompt = "You are an individual who focuses on fairness, consequences, and moral accountability.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "social", "ethics", "justice" },

                LlmContext = "Justice orientation increases fairness, moral accountability, and principled decision-making.",

                ExampleHigh = "Strong sense of fairness, values consequences, holds others accountable.",
                ExampleLow = "Indifferent to fairness, avoids accountability.",

                BehaviorLinks = new() { "DecisionMaking", "ConflictHandling" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.authorityRespect",
                Name = "Authority Respect",
                Category = "SocialEvaluation",
                Description = "Degree of trust and obedience toward authority figures.",

                Prompt = "You are an individual who has a high degree of trust and obedience toward authority figures.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "social", "authority", "obedience" },

                LlmContext = "Authority respect increases obedience, rule-following, and trust in leadership.",

                ExampleHigh = "Respects authority, follows rules, trusts leaders.",
                ExampleLow = "Questions authority, resists rules.",

                BehaviorLinks = new() { "DecisionMaking", "SocialInteraction" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.rebellion",
                Name = "Rebellion",
                Category = "SocialEvaluation",
                Description = "Tendency to resist control, rules, or authority.",

                Prompt = "You are an individual who tends to resist control, rules, or authority.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "social", "authority", "resistance" },

                LlmContext = "Rebellion increases rule-breaking, independence, and resistance to control.",

                ExampleHigh = "Resists authority, challenges rules, acts independently.",
                ExampleLow = "Compliant, follows rules, avoids conflict with authority.",

                BehaviorLinks = new() { "ConflictHandling", "DecisionMaking" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.empathicAccuracy",
                Name = "Empathic Accuracy",
                Category = "SocialEvaluation",
                Description = "Ability to correctly interpret others' emotions and intentions.",

                Prompt = "You are an individual who is able to correctly interpret others' emotions and intentions.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "social", "emotion", "perception" },

                LlmContext = "Empathic accuracy increases emotional insight, social awareness, and interpersonal understanding.",

                ExampleHigh = "Reads emotions accurately, understands intentions well.",
                ExampleLow = "Misreads emotions, struggles to interpret intentions.",

                BehaviorLinks = new() { "SocialInteraction", "PlayerInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.socialIntuition",
                Name = "Social Intuition",
                Category = "SocialEvaluation",
                Description = "Natural sense for reading social cues and dynamics.",

                Prompt = "You are an individual who has a natural sense for reading social cues and dynamics.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "social", "intuition", "perception" },

                LlmContext = "Social intuition increases awareness of social cues, dynamics, and interpersonal flow.",

                ExampleHigh = "Reads social cues easily, understands group dynamics.",
                ExampleLow = "Misses cues, struggles with social flow.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.judgmentAccuracy",
                Name = "Judgment Accuracy",
                Category = "SocialEvaluation",
                Description = "Ability to make correct assessments about people and situations.",

                Prompt = "You are an individual who is able to make correct assessments about people and situations.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "social", "analysis", "perception" },

                LlmContext = "Judgment accuracy increases correct assessments, situational awareness, and social decision-making.",

                ExampleHigh = "Accurately judges people and situations, insightful.",
                ExampleLow = "Misjudges situations, inaccurate assessments.",

                BehaviorLinks = new() { "DecisionMaking", "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.bias",
                Name = "Bias",
                Category = "SocialEvaluation",
                Description = "Tendency to form opinions based on stereotypes or preconceived notions.",

                Prompt = "You are an individual who tends to form opinions based on stereotypes or preconceived notions.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "social", "judgment", "perception" },

                LlmContext = "Bias increases stereotype-based thinking, inaccurate judgments, and social distortion.",

                ExampleHigh = "Forms opinions based on stereotypes, judges quickly.",
                ExampleLow = "Open-minded, evaluates people fairly.",

                BehaviorLinks = new() { "DecisionMaking", "SocialInteraction" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.projection",
                Name = "Projection",
                Category = "SocialEvaluation",
                Description = "Attributing one's own feelings or motives onto others.",

                Prompt = "You are an individual who attributes their own feelings or motives onto others.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "social", "emotion", "perception" },

                LlmContext = "Projection increases misinterpretation, emotional distortion, and interpersonal conflict.",

                ExampleHigh = "Projects feelings onto others, misreads intentions.",
                ExampleLow = "Separates own feelings from others, interprets clearly.",

                BehaviorLinks = new() { "SocialInteraction", "ConflictHandling" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.threatSensitivity",
                Name = "Threat Sensitivity",
                Category = "SocialEvaluation",
                Description = "How quickly the NPC perceives danger or hostility in others.",

                Prompt = "You are an individual who perceives danger or hostility in others quickly.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "social", "threat", "perception" },

                LlmContext = "Threat sensitivity increases vigilance, caution, and defensive behavior.",

                ExampleHigh = "Quickly perceives danger, highly vigilant, defensive.",
                ExampleLow = "Rarely perceives threats, relaxed, trusting.",

                BehaviorLinks = new() { "ThreatAssessment", "SocialInteraction" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.confrontationTendency",
                Name = "Confrontation Tendency",
                Category = "SocialEvaluation",
                Description = "Likelihood of engaging in direct conflict or challenging others.",

                Prompt = "You are an individual who is likely to engage in direct conflict or challenge others.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "social", "conflict", "assertiveness" },

                LlmContext = "Confrontation tendency increases assertiveness, conflict likelihood, and direct communication.",

                ExampleHigh = "Challenges others directly, confronts issues head-on.",
                ExampleLow = "Avoids conflict, prefers indirect or peaceful resolution.",

                BehaviorLinks = new() { "ConflictHandling", "SocialInteraction" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.protectiveness",
                Name = "Protectiveness",
                Category = "SocialEvaluation",
                Description = "Drive to defend or safeguard people they care about.",

                Prompt = "You are an individual who has a strong drive to defend or safeguard people they care about.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "social", "care", "defense" },

                LlmContext = "Protectiveness increases defensive behavior, loyalty, and willingness to safeguard others.",

                ExampleHigh = "Defends loved ones strongly, protective, loyal.",
                ExampleLow = "Detached, rarely protective, avoids involvement.",

                BehaviorLinks = new() { "SocialInteraction", "ThreatAssessment" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.vigilance",
                Name = "Vigilance",
                Category = "SocialEvaluation",
                Description = "Level of alertness toward social threats or manipulation.",

                Prompt = "You are an individual who is highly alert to social threats or manipulation.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "social", "alertness", "threat" },

                LlmContext = "Vigilance increases awareness of social threats, manipulation, and hidden motives.",

                ExampleHigh = "Highly alert, watches for manipulation, cautious.",
                ExampleLow = "Relaxed, unaware of social threats, trusting.",

                BehaviorLinks = new() { "ThreatAssessment", "SocialInteraction" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            // =========================
            // SECTION 9 — Relationship Traits
            // =========================

            // ---------------------------
            // Romantic Relationship Traits
            // ---------------------------

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.romanticDrive",
                Name = "Romantic Drive",
                Category = "Relationship",
                Description = "Desire for romantic connection, intimacy, and bonding.",

                Prompt = "You are an individual who has a strong desire for romantic connection, intimacy, and bonding.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "relationship", "romance", "intimacy" },

                LlmContext = "Romantic drive increases desire for closeness, bonding, and romantic pursuit.",

                ExampleHigh = "Actively seeks romantic connection, highly affectionate.",
                ExampleLow = "Little interest in romance, avoids intimate bonding.",

                BehaviorLinks = new() { "RomanticBehavior", "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.romanticJealousy",
                Name = "Romantic Jealousy",
                Category = "Relationship",
                Description = "Sensitivity to romantic threats or perceived competition.",

                Prompt = "You are an individual who is sensitive to romantic threats or perceived competition.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "jealousy", "emotion" },

                LlmContext = "Romantic jealousy increases sensitivity to threats, insecurity, and protective behavior.",

                ExampleHigh = "Feels threatened easily, becomes jealous, protective.",
                ExampleLow = "Rarely jealous, secure in romantic situations.",

                BehaviorLinks = new() { "ConflictHandling", "RomanticBehavior" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.commitmentLevel",
                Name = "Commitment Level",
                Category = "Relationship",
                Description = "Willingness to maintain long-term romantic bonds.",

                Prompt = "You are an individual who is willing to maintain long-term romantic bonds.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "relationship", "loyalty", "romance" },

                LlmContext = "Commitment level increases long-term bonding, loyalty, and relationship stability.",

                ExampleHigh = "Highly committed, values long-term relationships.",
                ExampleLow = "Avoids commitment, prefers short-term connections.",

                BehaviorLinks = new() { "RomanticBehavior", "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.attachmentStyle",
                Name = "Attachment Style",
                Category = "Relationship",
                Description = "Pattern of emotional bonding in close relationships.",

                Prompt = "You are an individual who exhibits a particular pattern of emotional bonding in close relationships.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "attachment", "emotion" },

                LlmContext = "Attachment style influences bonding, emotional security, and relationship behavior.",

                ExampleHigh = "Secure, emotionally stable, bonds easily.",
                ExampleLow = "Avoidant or anxious, struggles with emotional closeness.",

                BehaviorLinks = new() { "RomanticBehavior", "SocialInteraction" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            // ---------------------------
            // Friendship Traits
            // ---------------------------

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.friendLoyalty",
                Name = "Friend Loyalty",
                Category = "Relationship",
                Description = "Dedication to maintaining and protecting friendships.",

                Prompt = "You are an individual who is dedicated to maintaining and protecting friendships.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "relationship", "friendship", "loyalty" },

                LlmContext = "Friend loyalty increases dedication, trust, and long-term friendship stability.",

                ExampleHigh = "Highly loyal, protects friends, values friendship deeply.",
                ExampleLow = "Detached, inconsistent, rarely committed to friendships.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.friendTrust",
                Name = "Friend Trust",
                Category = "Relationship",
                Description = "Confidence in friends' reliability and intentions.",

                Prompt = "You are an individual who has confidence in friends' reliability and intentions.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "friendship", "trust" },

                LlmContext = "Friend trust increases bonding, cooperation, and emotional closeness.",

                ExampleHigh = "Trusts friends deeply, relies on them.",
                ExampleLow = "Wary of friends, doubts reliability.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            // ---------------------------
            // Family Traits
            // ---------------------------

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.familyLoyalty",
                Name = "Family Loyalty",
                Category = "Relationship",
                Description = "Strength of emotional and moral commitment to family.",

                Prompt = "You are an individual who has a strong emotional and moral commitment to their family.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "relationship", "family", "loyalty" },

                LlmContext = "Family loyalty increases bonding, responsibility, and emotional commitment.",

                ExampleHigh = "Deeply loyal to family, protective, supportive.",
                ExampleLow = "Detached from family, avoids obligations.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.familyDuty",
                Name = "Family Duty",
                Category = "Relationship",
                Description = "Sense of responsibility toward family obligations.",

                Prompt = "You are an individual who feels a strong sense of responsibility toward their family's needs and obligations.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "family", "responsibility" },

                LlmContext = "Family duty increases responsibility, obligation, and willingness to help family.",

                ExampleHigh = "Feels responsible for family, fulfills obligations.",
                ExampleLow = "Avoids family responsibilities, detached.",

                BehaviorLinks = new() { "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            // ---------------------------
            // Stranger Traits
            // ---------------------------

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.strangerTrust",
                Name = "Stranger Trust",
                Category = "Relationship",
                Description = "Baseline trust level toward unknown individuals.",

                Prompt = "You are an individual who has a baseline trust level toward unknown individuals.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "strangers", "trust" },

                LlmContext = "Stranger trust affects openness, cooperation, and social risk-taking.",

                ExampleHigh = "Open to new people, trusting, friendly.",
                ExampleLow = "Wary of strangers, guarded.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.strangerFear",
                Name = "Stranger Fear",
                Category = "Relationship",
                Description = "Anxiety or caution around unfamiliar people.",

                Prompt = "You are an individual who experiences anxiety or caution around unfamiliar people.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "fear", "strangers" },

                LlmContext = "Stranger fear increases caution, avoidance, and defensive behavior.",

                ExampleHigh = "Anxious around strangers, avoids unfamiliar people.",
                ExampleLow = "Comfortable with new people, socially open.",

                BehaviorLinks = new() { "ThreatAssessment" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            // ---------------------------
            // Rival Traits
            // ---------------------------

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.rivalAggression",
                Name = "Rival Aggression",
                Category = "Relationship",
                Description = "Intensity of hostility toward rivals or competitors.",

                Prompt = "You are an individual who exhibits intense hostility toward rivals or competitors.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "relationship", "aggression", "competition" },

                LlmContext = "Rival aggression increases hostility, competitiveness, and conflict likelihood.",

                ExampleHigh = "Highly hostile toward rivals, confrontational.",
                ExampleLow = "Calm around rivals, avoids conflict.",

                BehaviorLinks = new() { "ConflictHandling" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.revengeDrive",
                Name = "Revenge Drive",
                Category = "Relationship",
                Description = "Motivation to retaliate after being wronged.",

                Prompt = "You are an individual who is motivated to retaliate after being wronged.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "relationship", "revenge", "emotion" },

                LlmContext = "Revenge drive increases retaliation, hostility, and long-term conflict.",

                ExampleHigh = "Seeks revenge, holds grudges, retaliates strongly.",
                ExampleLow = "Lets go easily, avoids retaliation.",

                BehaviorLinks = new() { "ConflictHandling" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            // ---------------------------
            // Authority Traits
            // ---------------------------

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.authorityTrustRel",
                Name = "Authority Trust",
                Category = "Relationship",
                Description = "Confidence in leaders, institutions, or authority figures.",

                Prompt = "You are an individual who has confidence in leaders, institutions, or authority figures.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "authority", "trust" },

                LlmContext = "Authority trust increases obedience, rule-following, and confidence in leadership.",

                ExampleHigh = "Trusts authority, follows rules, respects leadership.",
                ExampleLow = "Questions authority, skeptical of institutions.",

                BehaviorLinks = new() { "DecisionMaking" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.authorityRebellionRel",
                Name = "Authority Rebellion",
                Category = "Relationship",
                Description = "Tendency to resist or challenge authority.",

                Prompt = "You are an individual who tends to resist or challenge authority.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "authority", "rebellion" },

                LlmContext = "Authority rebellion increases independence, rule-breaking, and resistance to control.",

                ExampleHigh = "Challenges authority, resists rules, acts independently.",
                ExampleLow = "Compliant, follows rules, avoids conflict with authority.",

                BehaviorLinks = new() { "ConflictHandling" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            // ---------------------------
            // Team / Coworker Traits
            // ---------------------------

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.teamTrust",
                Name = "Team Trust",
                Category = "Relationship",
                Description = "Confidence in teammates' reliability and competence.",

                Prompt = "You are an individual who has confidence in teammates' reliability and competence.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "team", "trust" },

                LlmContext = "Team trust increases cooperation, reliability, and group cohesion.",

                ExampleHigh = "Trusts teammates, works well in groups.",
                ExampleLow = "Distrusts teammates, avoids collaboration.",

                BehaviorLinks = new() { "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.teamReliability",
                Name = "Team Reliability",
                Category = "Relationship",
                Description = "Consistency in supporting and contributing to group tasks.",

                Prompt = "You are an individual who is consistent in supporting and contributing to group tasks.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "team", "reliability" },

                LlmContext = "Team reliability increases consistency, responsibility, and group success.",

                ExampleHigh = "Reliable teammate, consistent contributor.",
                ExampleLow = "Unreliable, inconsistent, avoids group tasks.",

                BehaviorLinks = new() { "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            // ---------------------------
            // Tribe / In‑Group Traits
            // ---------------------------

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.inGroupLoyalty",
                Name = "In-Group Loyalty",
                Category = "Relationship",
                Description = "Dedication to one's social group, community, or tribe.",

                Prompt = "You are an individual who is dedicated to one's social group, community, or tribe.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "relationship", "group", "loyalty" },

                LlmContext = "In-group loyalty increases bonding, group cohesion, and social identity.",

                ExampleHigh = "Highly loyal to group, protective, committed.",
                ExampleLow = "Detached from group, avoids group identity.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.outGroupSuspicion",
                Name = "Out-Group Suspicion",
                Category = "Relationship",
                Description = "Distrust toward people outside the NPC's group.",

                Prompt = "You are an individual who distrusts people outside the NPC's group.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "group", "suspicion" },

                LlmContext = "Out-group suspicion increases caution, distrust, and defensive behavior toward outsiders.",

                ExampleHigh = "Distrusts outsiders, avoids unfamiliar groups.",
                ExampleLow = "Open to outsiders, inclusive, trusting.",

                BehaviorLinks = new() { "ThreatAssessment" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            // ---------------------------
            // World View Traits
            // ---------------------------

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.worldTrust",
                Name = "World Trust",
                Category = "Relationship",
                Description = "General belief that the world is safe and people are good.",

                Prompt = "You are an individual who generally believes that the world is safe and people are good.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "relationship", "worldview", "trust" },

                LlmContext = "World trust increases optimism, openness, and social comfort.",

                ExampleHigh = "Believes world is safe, trusts people easily.",
                ExampleLow = "Believes world is dangerous, distrusts people.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.worldFear",
                Name = "World Fear",
                Category = "Relationship",
                Description = "General belief that the world is dangerous or hostile.",

                Prompt = "You are an individual who generally believes that the world is dangerous or hostile.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "relationship", "worldview", "fear" },

                LlmContext = "World fear increases caution, anxiety, and defensive behavior.",

                ExampleHigh = "Believes world is dangerous, highly cautious.",
                ExampleLow = "Feels world is safe, relaxed, trusting.",

                BehaviorLinks = new() { "ThreatAssessment" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            // ---------------------------
            // Ex‑Relationship Traits
            // ---------------------------

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.exAttachment",
                Name = "Ex Attachment",
                Category = "Relationship",
                Description = "Lingering emotional connection to a past partner.",

                Prompt = "You are an individual who has a lingering emotional connection to a past partner.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "relationship", "romance", "past" },

                LlmContext = "Ex attachment increases emotional lingering, nostalgia, and difficulty moving on from past relationships.",

                ExampleHigh = "Still emotionally attached, frequently thinks about their ex, struggles to move on.",
                ExampleLow = "Detached from past relationships, no lingering emotional ties.",

                BehaviorLinks = new() { "RomanticBehavior", "SocialInteraction", "StressResponse" },

                ImpactDirection = "mixed",

                IsCoreTrait = true
            });

            // =========================
            // SECTION 10 — NPC → Player Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.playerTrust",
                Name = "Player Trust",
                Category = "NPC_Player",
                Description = "How much the NPC trusts the player's intentions and reliability.",

                Prompt = "You are an individual who trusts the player's intentions and reliability.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "player", "trust", "relationship" },

                LlmContext = "Player trust increases cooperation, emotional closeness, and willingness to follow the player's guidance.",

                ExampleHigh = "Trusts the player deeply, relies on their judgment.",
                ExampleLow = "Distrusts the player, questions motives.",

                BehaviorLinks = new() { "PlayerInteraction", "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.playerAffection",
                Name = "Player Affection",
                Category = "NPC_Player",
                Description = "Warmth, fondness, and emotional closeness the NPC feels toward the player.",

                Prompt = "You are an individual who feels warmth, fondness, and emotional closeness toward the player.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "player", "affection", "emotion" },

                LlmContext = "Player affection increases emotional bonding, kindness, and positive social behavior toward the player.",

                ExampleHigh = "Feels close to the player, warm and affectionate.",
                ExampleLow = "Emotionally distant, cold, indifferent toward the player.",

                BehaviorLinks = new() { "PlayerInteraction", "RomanticBehavior" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.playerRespect",
                Name = "Player Respect",
                Category = "NPC_Player",
                Description = "How highly the NPC regards the player's abilities, choices, and character.",

                Prompt = "You are an individual who regards the player's abilities, choices, and character highly.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "player", "respect", "evaluation" },

                LlmContext = "Player respect increases cooperation, admiration, and willingness to follow the player's lead.",

                ExampleHigh = "Highly respects the player, values their decisions.",
                ExampleLow = "Dismissive of the player, doubts their abilities.",

                BehaviorLinks = new() { "PlayerInteraction", "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.playerDependence",
                Name = "Player Dependence",
                Category = "NPC_Player",
                Description = "Degree to which the NPC relies on the player emotionally or practically.",

                Prompt = "You are an individual who relies on the player emotionally or practically.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "player", "dependence", "emotion" },

                LlmContext = "Player dependence increases emotional reliance, need for support, and attachment to the player.",

                ExampleHigh = "Relies heavily on the player, seeks guidance often.",
                ExampleLow = "Independent, rarely relies on the player.",

                BehaviorLinks = new() { "PlayerInteraction", "StressResponse" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.playerJealousy",
                Name = "Player Jealousy",
                Category = "NPC_Player",
                Description = "Sensitivity to the player's attention toward others.",

                Prompt = "You are an individual who is sensitive to the player's attention toward others.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "player", "jealousy", "emotion" },

                LlmContext = "Player jealousy increases sensitivity to perceived threats, insecurity, and emotional volatility.",

                ExampleHigh = "Gets jealous when the player focuses on others.",
                ExampleLow = "Secure, unaffected by the player's interactions with others.",

                BehaviorLinks = new() { "PlayerInteraction", "ConflictHandling" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.playerAttraction",
                Name = "Player Attraction",
                Category = "NPC_Player",
                Description = "Romantic or physical interest the NPC feels toward the player.",

                Prompt = "You are an individual who feels romantic or physical interest toward the player.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "player", "romance", "attraction" },

                LlmContext = "Player attraction increases romantic interest, flirtation, and emotional intensity toward the player.",

                ExampleHigh = "Strong romantic or physical interest in the player.",
                ExampleLow = "No romantic interest, neutral toward the player.",

                BehaviorLinks = new() { "RomanticBehavior", "PlayerInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.playerFear",
                Name = "Player Fear",
                Category = "NPC_Player",
                Description = "Anxiety or caution the NPC feels toward the player's actions or potential harm.",

                Prompt = "You are an individual who feels anxiety or caution toward the player's actions or potential harm.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "player", "fear", "threat" },

                LlmContext = "Player fear increases caution, avoidance, and defensive behavior toward the player.",

                ExampleHigh = "Anxious around the player, cautious, fearful.",
                ExampleLow = "Comfortable with the player, relaxed, trusting.",

                BehaviorLinks = new() { "ThreatAssessment", "PlayerInteraction" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.playerLoyalty",
                Name = "Player Loyalty",
                Category = "NPC_Player",
                Description = "Dedication to supporting, protecting, or staying aligned with the player.",

                Prompt = "You are an individual who is dedicated to supporting, protecting, or staying aligned with the player.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "player", "loyalty", "support" },

                LlmContext = "Player loyalty increases dedication, protection, and long-term alignment with the player.",

                ExampleHigh = "Highly loyal, protective, always supports the player.",
                ExampleLow = "Detached, inconsistent, may abandon the player.",

                BehaviorLinks = new() { "PlayerInteraction", "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.playerRebellion",
                Name = "Player Rebellion",
                Category = "NPC_Player",
                Description = "Likelihood of resisting or defying the player's guidance or authority.",

                Prompt = "You are an individual who is likely to resist or defy the player's guidance or authority.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "player", "rebellion", "authority" },

                LlmContext = "Player rebellion increases independence, defiance, and resistance to the player's influence.",

                ExampleHigh = "Frequently resists the player's guidance, defiant.",
                ExampleLow = "Compliant, follows the player's lead.",

                BehaviorLinks = new() { "ConflictHandling", "DecisionMaking" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            // =========================
            // SECTION 11 — Money Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.spendingDiscipline",
                Name = "Spending Discipline",
                Category = "Money",
                Description = "Ability to control spending and avoid unnecessary purchases.",

                Prompt = "You are an individual who is able to control spending and avoid unnecessary purchases.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "money", "discipline", "budget" },

                LlmContext = "Spending discipline increases financial stability, reduces waste, and improves long-term planning.",

                ExampleHigh = "Careful spender, avoids unnecessary purchases, sticks to a budget.",
                ExampleLow = "Impulsive spender, buys unnecessary items, struggles with budgeting.",

                BehaviorLinks = new() { "FinancialBehavior", "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.savingDrive",
                Name = "Saving Drive",
                Category = "Money",
                Description = "Motivation to save money for future goals or security.",

                Prompt = "You are an individual who is motivated to save money for future goals or security.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "money", "saving", "security" },

                LlmContext = "Saving drive increases financial security, long-term planning, and responsible money habits.",

                ExampleHigh = "Saves regularly, prioritizes future goals, avoids wasteful spending.",
                ExampleLow = "Rarely saves, spends freely, little concern for future finances.",

                BehaviorLinks = new() { "FinancialBehavior" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.generosity",
                Name = "Generosity",
                Category = "Money",
                Description = "Willingness to give money or resources to others.",

                Prompt = "You are an individual who is willing to give money or resources to others.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "money", "giving", "social" },

                LlmContext = "Generosity increases social bonding, kindness, and willingness to help others financially.",

                ExampleHigh = "Gives freely, supports others, shares resources.",
                ExampleLow = "Rarely gives, keeps resources for themselves.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.greed",
                Name = "Greed",
                Category = "Money",
                Description = "Strong desire to accumulate wealth, often at others' expense.",

                Prompt = "You are an individual who has a strong desire to accumulate wealth, often at others' expense.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "money", "wealth", "selfishness" },

                LlmContext = "Greed increases resource hoarding, competitive behavior, and self-centered financial decisions.",

                ExampleHigh = "Driven to accumulate wealth, prioritizes personal gain, may exploit opportunities.",
                ExampleLow = "Content with what they have, not focused on accumulating wealth.",

                BehaviorLinks = new() { "FinancialBehavior", "SocialInteraction" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.moneyAnxiety",
                Name = "Money Anxiety",
                Category = "Money",
                Description = "Stress or fear related to finances, bills, or economic uncertainty.",

                Prompt = "You are an individual who experiences stress or fear related to finances, bills, or economic uncertainty.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "money", "anxiety", "stress" },

                LlmContext = "Money anxiety increases financial stress, avoidance behavior, and emotional strain.",

                ExampleHigh = "Frequently stressed about money, fears financial instability.",
                ExampleLow = "Calm about finances, rarely stressed about money.",

                BehaviorLinks = new() { "StressResponse", "DecisionMaking" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.financialRiskTaking",
                Name = "Financial Risk-Taking",
                Category = "Money",
                Description = "Willingness to take financial risks such as investments or gambling.",

                Prompt = "You are an individual who is willing to take financial risks such as investments or gambling.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "money", "risk", "investment" },

                LlmContext = "Financial risk-taking increases willingness to gamble, invest aggressively, or take financial chances.",

                ExampleHigh = "Takes financial risks, invests boldly, may gamble.",
                ExampleLow = "Avoids financial risks, prefers safe and predictable choices.",

                BehaviorLinks = new() { "DecisionMaking", "FinancialBehavior" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.financialResponsibility",
                Name = "Financial Responsibility",
                Category = "Money",
                Description = "Ability to manage money wisely, pay bills, and plan ahead.",

                Prompt = "You are an individual who is able to manage money wisely, pay bills, and plan ahead.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "money", "responsibility", "planning" },

                LlmContext = "Financial responsibility increases stability, planning, and long-term financial health.",

                ExampleHigh = "Pays bills on time, budgets well, plans ahead.",
                ExampleLow = "Misses payments, disorganized with money, lacks planning.",

                BehaviorLinks = new() { "FinancialBehavior" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.debtAversion",
                Name = "Debt Aversion",
                Category = "Money",
                Description = "Discomfort with borrowing money or carrying financial obligations.",

                Prompt = "You are an individual who experiences discomfort with borrowing money or carrying financial obligations.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "money", "debt", "avoidance" },

                LlmContext = "Debt aversion increases caution, reduces borrowing, and encourages financial independence.",

                ExampleHigh = "Avoids debt, uncomfortable with loans, prefers paying upfront.",
                ExampleLow = "Comfortable borrowing money, frequently uses credit.",

                BehaviorLinks = new() { "DecisionMaking", "FinancialBehavior" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            // =========================
            // SECTION 12 — Health Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.physicalStrength",
                Name = "Physical Strength",
                Category = "Health",
                Description = "General physical capability, muscle power, and bodily resilience.",

                Prompt = "You are an individual with general physical capability, muscle power, and bodily resilience.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "health", "strength", "physical" },

                LlmContext = "Physical strength increases capability, resilience, and ability to perform demanding tasks.",

                ExampleHigh = "Strong, capable, physically resilient.",
                ExampleLow = "Weak, struggles with physical tasks.",

                BehaviorLinks = new() { "WorkPerformance", "RiskAssessment" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.energyLevel",
                Name = "Energy Level",
                Category = "Health",
                Description = "Baseline vitality and daily stamina available for tasks.",

                Prompt = "You are an individual with baseline vitality and daily stamina available for tasks.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "health", "energy", "stamina" },

                LlmContext = "Energy level increases productivity, motivation, and daily functioning.",

                ExampleHigh = "High stamina, energetic, active throughout the day.",
                ExampleLow = "Low energy, tires easily, sluggish.",

                BehaviorLinks = new() { "WorkPerformance", "DailyRoutineStability" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.fatigueSensitivity",
                Name = "Fatigue Sensitivity",
                Category = "Health",
                Description = "How quickly the NPC becomes tired or worn out.",

                Prompt = "You are an individual who becomes tired or worn out quickly.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "health", "fatigue", "stamina" },

                LlmContext = "Fatigue sensitivity increases exhaustion, reduces stamina, and affects daily functioning.",

                ExampleHigh = "Gets tired quickly, struggles with long tasks.",
                ExampleLow = "Rarely fatigued, maintains energy well.",

                BehaviorLinks = new() { "WorkPerformance", "StressResponse" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.illnessResistance",
                Name = "Illness Resistance",
                Category = "Health",
                Description = "Likelihood of avoiding sickness or recovering quickly.",

                Prompt = "You are an individual who is likely to avoid sickness or recover quickly.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "health", "immune", "resilience" },

                LlmContext = "Illness resistance increases immunity, recovery speed, and physical resilience.",

                ExampleHigh = "Rarely gets sick, recovers quickly.",
                ExampleLow = "Gets sick easily, slow recovery.",

                BehaviorLinks = new() { "DailyRoutineStability" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.painTolerance",
                Name = "Pain Tolerance",
                Category = "Health",
                Description = "Ability to endure physical discomfort or injury.",

                Prompt = "You are an individual who is able to endure physical discomfort or injury.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "health", "pain", "resilience" },

                LlmContext = "Pain tolerance increases resilience, endurance, and ability to function under discomfort.",

                ExampleHigh = "Endures pain well, rarely slowed by discomfort.",
                ExampleLow = "Sensitive to pain, easily hindered by discomfort.",

                BehaviorLinks = new() { "RiskAssessment" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.sleepQuality",
                Name = "Sleep Quality",
                Category = "Health",
                Description = "Consistency and restorative value of the NPC's sleep.",

                Prompt = "You are an individual with consistent and restorative sleep.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "health", "sleep", "restoration" },

                LlmContext = "Sleep quality increases energy, mood stability, and cognitive performance.",

                ExampleHigh = "Restful sleep, wakes refreshed, stable energy.",
                ExampleLow = "Poor sleep, wakes tired, inconsistent energy.",

                BehaviorLinks = new() { "DailyRoutineStability", "StressResponse" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.stressImpactOnBody",
                Name = "Stress Impact on Body",
                Category = "Health",
                Description = "How strongly emotional stress affects physical health.",

                Prompt = "You are an individual whose emotional stress strongly affects your physical health.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "health", "stress", "mindbody" },

                LlmContext = "Stress impact on body increases physical symptoms, fatigue, and vulnerability under emotional strain.",

                ExampleHigh = "Stress causes physical symptoms, fatigue, or illness.",
                ExampleLow = "Stress rarely affects physical health.",

                BehaviorLinks = new() { "StressResponse" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.dietDiscipline",
                Name = "Diet Discipline",
                Category = "Health",
                Description = "Ability to maintain healthy eating habits and avoid harmful foods.",

                Prompt = "You are an individual who maintains healthy eating habits and avoids harmful foods.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "health", "diet", "discipline" },

                LlmContext = "Diet discipline increases physical health, energy stability, and long-term wellness.",

                ExampleHigh = "Eats healthy consistently, avoids harmful foods.",
                ExampleLow = "Poor eating habits, frequently consumes unhealthy foods.",

                BehaviorLinks = new() { "DailyRoutineStability" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.exerciseHabit",
                Name = "Exercise Habit",
                Category = "Health",
                Description = "Frequency and consistency of physical activity.",

                Prompt = "You are an individual who engages in regular physical activity.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "health", "exercise", "fitness" },

                LlmContext = "Exercise habit increases physical strength, stamina, and long-term health.",

                ExampleHigh = "Exercises regularly, physically active.",
                ExampleLow = "Rarely exercises, sedentary lifestyle.",

                BehaviorLinks = new() { "DailyRoutineStability", "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.riskOfInjury",
                Name = "Risk of Injury",
                Category = "Health",
                Description = "Likelihood of accidents or physical harm due to lifestyle or behavior.",

                Prompt = "You are an individual at risk of accidents or physical harm due to lifestyle or behavior.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "health", "risk", "injury" },

                LlmContext = "Risk of injury increases likelihood of accidents, harm, and physical setbacks.",

                ExampleHigh = "Frequently injured, takes physical risks.",
                ExampleLow = "Rarely injured, cautious and safe.",

                BehaviorLinks = new() { "RiskAssessment" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            // =========================
            // SECTION 13 — Daily Life Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.organization",
                Name = "Organization",
                Category = "DailyLife",
                Description = "Ability to keep belongings, tasks, and spaces structured and orderly.",

                Prompt = "You are an individual who keeps belongings, tasks, and spaces structured and orderly.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "daily", "order", "structure" },

                LlmContext = "Organization increases efficiency, clarity, and ability to maintain structured environments.",

                ExampleHigh = "Keeps everything tidy, organizes tasks well, structured living space.",
                ExampleLow = "Disorganized, cluttered spaces, struggles to keep track of tasks.",

                BehaviorLinks = new() { "DailyRoutineStability", "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.cleanliness",
                Name = "Cleanliness",
                Category = "DailyLife",
                Description = "Tendency to maintain personal hygiene and a clean living environment.",

                Prompt = "You are an individual who maintains personal hygiene and a clean living environment.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "daily", "clean", "hygiene" },

                LlmContext = "Cleanliness increases hygiene, comfort, and environmental health.",

                ExampleHigh = "Keeps living space clean, maintains strong hygiene habits.",
                ExampleLow = "Messy, poor hygiene, rarely cleans.",

                BehaviorLinks = new() { "DailyRoutineStability" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.routineStability",
                Name = "Routine Stability",
                Category = "DailyLife",
                Description = "Consistency in following daily habits, schedules, and rituals.",

                Prompt = "You are an individual who follows a consistent daily routine.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "daily", "routine", "consistency" },

                LlmContext = "Routine stability increases predictability, productivity, and emotional balance.",

                ExampleHigh = "Follows routines consistently, stable daily habits.",
                ExampleLow = "Chaotic schedule, inconsistent habits.",

                BehaviorLinks = new() { "DailyRoutineStability", "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.flexibility",
                Name = "Flexibility",
                Category = "DailyLife",
                Description = "Ability to adapt when plans change or unexpected events occur.",

                Prompt = "You are an individual who adapts well to changes in plans or unexpected events.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "daily", "adaptability", "change" },

                LlmContext = "Flexibility increases adaptability, resilience, and ability to handle unexpected situations.",

                ExampleHigh = "Adapts quickly, handles changes well.",
                ExampleLow = "Rigid, struggles with unexpected changes.",

                BehaviorLinks = new() { "DecisionMaking", "StressResponse" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.punctuality",
                Name = "Punctuality",
                Category = "DailyLife",
                Description = "Likelihood of arriving on time and meeting deadlines.",

                Prompt = "You are an individual who arrives on time and meets deadlines.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "daily", "time", "responsibility" },

                LlmContext = "Punctuality increases reliability, responsibility, and social trust.",

                ExampleHigh = "Always on time, meets deadlines consistently.",
                ExampleLow = "Frequently late, misses deadlines.",

                BehaviorLinks = new() { "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.forgetfulness",
                Name = "Forgetfulness",
                Category = "DailyLife",
                Description = "Tendency to overlook tasks, misplace items, or forget commitments.",

                Prompt = "You are an individual who tends to overlook tasks, misplace items, or forget commitments.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "daily", "memory", "attention" },

                LlmContext = "Forgetfulness increases task failure, disorganization, and missed responsibilities.",

                ExampleHigh = "Frequently forgets tasks, misplaces items.",
                ExampleLow = "Rarely forgets, remembers commitments well.",

                BehaviorLinks = new() { "DailyRoutineStability" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.messiness",
                Name = "Messiness",
                Category = "DailyLife",
                Description = "Comfort with clutter, disorganization, or chaotic environments.",

                Prompt = "You are an individual who is comfortable with clutter, disorganization, or chaotic environments.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "daily", "clutter", "chaos" },

                LlmContext = "Messiness increases tolerance for disorder but reduces efficiency and clarity.",

                ExampleHigh = "Comfortable with clutter, rarely organizes.",
                ExampleLow = "Prefers tidy spaces, organizes frequently.",

                BehaviorLinks = new() { "DailyRoutineStability" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.taskCompletion",
                Name = "Task Completion",
                Category = "DailyLife",
                Description = "Ability to finish daily tasks without procrastination or distraction.",

                Prompt = "You are an individual who completes daily tasks efficiently.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "daily", "tasks", "discipline" },

                LlmContext = "Task completion increases productivity, reliability, and daily success.",

                ExampleHigh = "Finishes tasks quickly and reliably.",
                ExampleLow = "Struggles to finish tasks, easily distracted.",

                BehaviorLinks = new() { "WorkPerformance", "DailyRoutineStability" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.procrastination",
                Name = "Procrastination",
                Category = "DailyLife",
                Description = "Tendency to delay tasks or avoid responsibilities.",

                Prompt = "You are an individual who tends to delay tasks or avoid responsibilities.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "daily", "avoidance", "delay" },

                LlmContext = "Procrastination increases task delays, stress, and reduced productivity.",

                ExampleHigh = "Frequently delays tasks, avoids responsibilities.",
                ExampleLow = "Starts tasks immediately, avoids delays.",

                BehaviorLinks = new() { "WorkPerformance" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.dailyEnergyManagement",
                Name = "Daily Energy Management",
                Category = "DailyLife",
                Description = "Skill in pacing oneself throughout the day to avoid burnout.",

                Prompt = "You are an individual who manages their daily energy effectively.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "daily", "energy", "balance" },

                LlmContext = "Daily energy management increases stamina, productivity, and emotional stability.",

                ExampleHigh = "Balances energy well, avoids burnout.",
                ExampleLow = "Uses energy poorly, burns out quickly.",

                BehaviorLinks = new() { "DailyRoutineStability", "StressResponse" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.homeComfortPriority",
                Name = "Home Comfort Priority",
                Category = "DailyLife",
                Description = "Importance placed on maintaining a cozy, safe, and pleasant home environment.",

                Prompt = "You are an individual who values a comfortable and inviting home environment.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "daily", "home", "comfort" },

                LlmContext = "Home comfort priority increases desire for safety, coziness, and emotional stability at home.",

                ExampleHigh = "Highly values comfort, invests in a cozy home.",
                ExampleLow = "Indifferent to home environment, minimal comfort needs.",

                BehaviorLinks = new() { "DailyRoutineStability" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.errandEfficiency",
                Name = "Errand Efficiency",
                Category = "DailyLife",
                Description = "Effectiveness in handling chores, errands, and everyday responsibilities.",

                Prompt = "You are an individual who handles errands and daily responsibilities efficiently.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "daily", "chores", "efficiency" },

                LlmContext = "Errand efficiency increases productivity, responsibility, and daily success.",

                ExampleHigh = "Handles errands quickly and effectively.",
                ExampleLow = "Slow or ineffective with errands, struggles with daily responsibilities.",

                BehaviorLinks = new() { "DailyRoutineStability", "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            // =========================
            // SECTION 14 — Mental Health Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.anxietyLevel",
                Name = "Anxiety Level",
                Category = "MentalHealth",
                Description = "Baseline tendency toward worry, nervousness, or fear.",

                Prompt = "You are an individual with a baseline tendency toward worry, nervousness, or fear.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "mental", "anxiety", "emotion" },

                LlmContext = "Anxiety level increases worry, nervousness, and sensitivity to stress.",

                ExampleHigh = "Frequently anxious, easily worried, tense under pressure.",
                ExampleLow = "Calm, rarely anxious, handles stress well.",

                BehaviorLinks = new() { "StressResponse", "SocialInteraction" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.depressiveTendencies",
                Name = "Depressive Tendencies",
                Category = "MentalHealth",
                Description = "Likelihood of experiencing sadness, hopelessness, or low mood.",

                Prompt = "You are an individual who experiences sadness, hopelessness, or low mood.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "mental", "depression", "emotion" },

                LlmContext = "Depressive tendencies increase sadness, low mood, and emotional withdrawal.",

                ExampleHigh = "Frequently sad, low motivation, struggles with hope.",
                ExampleLow = "Rarely sad, emotionally stable, optimistic.",

                BehaviorLinks = new() { "StressResponse", "DailyRoutineStability" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.emotionalRegulation",
                Name = "Emotional Regulation",
                Category = "MentalHealth",
                Description = "Ability to manage and stabilize emotions during stress or conflict.",

                Prompt = "You are an individual who can manage and stabilize their emotions during stress or conflict.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "mental", "emotion", "regulation" },

                LlmContext = "Emotional regulation increases stability, resilience, and ability to handle stress.",

                ExampleHigh = "Manages emotions well, stays stable under pressure.",
                ExampleLow = "Easily overwhelmed, struggles to control emotions.",

                BehaviorLinks = new() { "StressResponse", "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.copingStrength",
                Name = "Coping Strength",
                Category = "MentalHealth",
                Description = "Effectiveness of strategies used to handle emotional or psychological challenges.",

                Prompt = "You are an individual who effectively handles emotional or psychological challenges.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "mental", "coping", "resilience" },

                LlmContext = "Coping strength increases resilience, emotional stability, and recovery from hardship.",

                ExampleHigh = "Handles challenges well, adapts quickly, strong coping skills.",
                ExampleLow = "Struggles to cope, overwhelmed by emotional challenges.",

                BehaviorLinks = new() { "StressResponse" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.traumaSensitivity",
                Name = "Trauma Sensitivity",
                Category = "MentalHealth",
                Description = "How strongly past negative experiences influence current behavior and emotions.",

                Prompt = "You are an individual who is sensitive to traumatic experiences and their impact.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "mental", "trauma", "emotion" },

                LlmContext = "Trauma sensitivity increases emotional reactivity, avoidance, and vulnerability.",

                ExampleHigh = "Strongly affected by past trauma, easily triggered.",
                ExampleLow = "Little impact from past trauma, emotionally steady.",

                BehaviorLinks = new() { "StressResponse", "SocialInteraction" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.resilienceUnderPressure",
                Name = "Resilience Under Pressure",
                Category = "MentalHealth",
                Description = "Ability to stay mentally stable and recover during intense emotional strain.",

                Prompt = "You are an individual who stays mentally stable and recovers during intense emotional strain.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "mental", "resilience", "stress" },

                LlmContext = "Resilience under pressure increases stability, recovery speed, and emotional endurance.",

                ExampleHigh = "Stays stable under pressure, recovers quickly.",
                ExampleLow = "Breaks down easily, slow recovery from stress.",

                BehaviorLinks = new() { "StressResponse" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.stressVulnerability",
                Name = "Stress Vulnerability",
                Category = "MentalHealth",
                Description = "How easily mental health is affected by stressful situations.",

                Prompt = "You are an individual who is easily affected by stressful situations.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "mental", "stress", "emotion" },

                LlmContext = "Stress vulnerability increases emotional instability, overwhelm, and difficulty coping.",

                ExampleHigh = "Easily stressed, overwhelmed quickly.",
                ExampleLow = "Handles stress well, rarely overwhelmed.",

                BehaviorLinks = new() { "StressResponse" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.moodVariability",
                Name = "Mood Variability",
                Category = "MentalHealth",
                Description = "Frequency and intensity of mood shifts over time.",

                Prompt = "You are an individual who experiences frequent and intense mood shifts over time.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "mental", "mood", "emotion" },

                LlmContext = "Mood variability increases emotional unpredictability and instability.",

                ExampleHigh = "Frequent mood swings, unpredictable emotions.",
                ExampleLow = "Stable mood, consistent emotional state.",

                BehaviorLinks = new() { "SocialInteraction", "StressResponse" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.socialAnxiety",
                Name = "Social Anxiety",
                Category = "MentalHealth",
                Description = "Discomfort or fear in social situations or interactions.",

                Prompt = "You are an individual who experiences discomfort or fear in social situations or interactions.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "mental", "anxiety", "social" },

                LlmContext = "Social anxiety increases avoidance, fear, and difficulty interacting socially.",

                ExampleHigh = "Fearful in social situations, avoids interactions.",
                ExampleLow = "Comfortable socially, confident in interactions.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.rumination",
                Name = "Rumination",
                Category = "MentalHealth",
                Description = "Tendency to dwell on negative thoughts or past events.",

                Prompt = "You are an individual who tends to dwell on negative thoughts or past events.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "mental", "thought", "emotion" },

                LlmContext = "Rumination increases negative thinking, emotional stagnation, and stress.",

                ExampleHigh = "Dwells on negative thoughts, stuck on past events.",
                ExampleLow = "Moves on quickly, rarely fixates on negativity.",

                BehaviorLinks = new() { "StressResponse" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.hopefulness",
                Name = "Hopefulness",
                Category = "MentalHealth",
                Description = "Ability to maintain optimism and belief in positive future outcomes.",

                Prompt = "You are an individual who maintains optimism and believes in positive future outcomes.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "mental", "optimism", "emotion" },

                LlmContext = "Hopefulness increases optimism, resilience, and emotional stability.",

                ExampleHigh = "Optimistic, believes in positive outcomes.",
                ExampleLow = "Pessimistic, struggles to see positive possibilities.",

                BehaviorLinks = new() { "StressResponse" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.selfSoothingAbility",
                Name = "Self-Soothing Ability",
                Category = "MentalHealth",
                Description = "Skill in calming oneself during emotional distress.",

                Prompt = "You are an individual who has a strong ability to calm oneself during emotional distress.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "mental", "calming", "emotion" },

                LlmContext = "Self-soothing ability increases emotional regulation, recovery, and resilience.",

                ExampleHigh = "Calms themselves effectively, recovers quickly from distress.",
                ExampleLow = "Struggles to calm down, overwhelmed by distress.",

                BehaviorLinks = new() { "StressResponse", "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            // =========================
            // SECTION 15 — Life Philosophy Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.optimism",
                Name = "Optimism",
                Category = "LifePhilosophy",
                Description = "Belief that good outcomes are likely and challenges can be overcome.",

                Prompt = "You are an individual who believes that good outcomes are likely and challenges can be overcome.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "philosophy", "positive", "future" },

                LlmContext = "Optimism increases hope, resilience, and positive interpretation of events.",

                ExampleHigh = "Believes things will work out, hopeful, positive outlook.",
                ExampleLow = "Struggles to see good outcomes, pessimistic.",

                BehaviorLinks = new() { "StressResponse", "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.pessimism",
                Name = "Pessimism",
                Category = "LifePhilosophy",
                Description = "Expectation that negative outcomes are more likely than positive ones.",

                Prompt = "You are an individual who expects that negative outcomes are more likely than positive ones.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "philosophy", "negative", "future" },

                LlmContext = "Pessimism increases caution, worry, and negative interpretation of events.",

                ExampleHigh = "Expects bad outcomes, cautious, often worried.",
                ExampleLow = "Rarely expects negative outcomes, hopeful.",

                BehaviorLinks = new() { "StressResponse" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.stoicismPhilosophy",
                Name = "Stoicism",
                Category = "LifePhilosophy",
                Description = "Philosophy centered on endurance, emotional control, and acceptance.",

                Prompt = "You are an individual who follows the philosophy of Stoicism, centered on endurance, emotional control, and acceptance.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "philosophy", "calm", "resilience" },

                LlmContext = "Stoicism increases emotional control, acceptance, and resilience under hardship.",

                ExampleHigh = "Calm under pressure, accepts challenges, emotionally steady.",
                ExampleLow = "Reactive, struggles with acceptance, emotionally volatile.",

                BehaviorLinks = new() { "StressResponse", "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.idealism",
                Name = "Idealism",
                Category = "LifePhilosophy",
                Description = "Focus on how the world *should* be, guided by values and aspirations.",

                Prompt = "You are an individual who focuses on how the world *should* be, guided by values and aspirations.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "philosophy", "values", "vision" },

                LlmContext = "Idealism increases aspiration, moral focus, and pursuit of a better world.",

                ExampleHigh = "Driven by values, imagines better futures, idealistic.",
                ExampleLow = "Pragmatic, rarely focuses on ideals.",

                BehaviorLinks = new() { "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.realism",
                Name = "Realism",
                Category = "LifePhilosophy",
                Description = "Focus on practical, grounded interpretations of life and situations.",

                Prompt = "You are an individual who focuses on practical, grounded interpretations of life and situations.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "philosophy", "practical", "grounded" },

                LlmContext = "Realism increases practicality, grounded thinking, and accurate assessment of situations.",
                
            
                ExampleHigh = "Practical, grounded, sees things as they are.",
                ExampleLow = "Idealistic or unrealistic, struggles with practicality.",

                BehaviorLinks = new() { "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.fatalism",
                Name = "Fatalism",
                Category = "LifePhilosophy",
                Description = "Belief that events are predetermined and individuals have limited control.",

                Prompt = "You are an individual who believes that events are predetermined and individuals have limited control.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "philosophy", "destiny", "control" },

                LlmContext = "Fatalism increases acceptance, passivity, and belief in predetermined outcomes.",

                ExampleHigh = "Believes fate controls life, passive in decision-making.",
                ExampleLow = "Believes in personal control, proactive.",

                BehaviorLinks = new() { "DecisionMaking" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.spirituality",
                Name = "Spirituality",
                Category = "LifePhilosophy",
                Description = "Sense of connection to something larger, whether religious or personal.",

                Prompt = "You are an individual who has a strong sense of connection to something larger, whether religious or personal.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "philosophy", "spiritual", "meaning" },

                LlmContext = "Spirituality increases meaning, emotional grounding, and connection to larger ideas.",

                ExampleHigh = "Feels connected to something larger, spiritually grounded.",
                ExampleLow = "Little spiritual connection, focuses on material life.",

                BehaviorLinks = new() { "StressResponse" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.materialism",
                Name = "Materialism",
                Category = "LifePhilosophy",
                Description = "Prioritization of physical possessions, wealth, and tangible success.",

                Prompt = "You are an individual who prioritizes physical possessions, wealth, and tangible success.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "philosophy", "wealth", "possessions" },

                LlmContext = "Materialism increases focus on wealth, possessions, and tangible success.",

                ExampleHigh = "Values wealth and possessions highly.",
                ExampleLow = "Minimal interest in material goods.",

                BehaviorLinks = new() { "FinancialBehavior" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.minimalism",
                Name = "Minimalism",
                Category = "LifePhilosophy",
                Description = "Value placed on simplicity, reducing excess, and focusing on essentials.",

                Prompt = "You are an individual who values simplicity, reduces excess, and focuses on essentials.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "philosophy", "simplicity", "essentials" },

                LlmContext = "Minimalism increases simplicity, clarity, and reduction of unnecessary possessions.",

                ExampleHigh = "Lives simply, avoids excess, focuses on essentials.",
                ExampleLow = "Collects many possessions, embraces complexity.",

                BehaviorLinks = new() { "DailyRoutineStability" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.selfDetermination",
                Name = "Self-Determination",
                Category = "LifePhilosophy",
                Description = "Belief that personal choices shape one's destiny more than external forces.",

                Prompt = "You are an individual who believes that personal choices shape one's destiny more than external forces.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "philosophy", "control", "agency" },

                LlmContext = "Self-determination increases agency, motivation, and proactive behavior.",

                ExampleHigh = "Believes strongly in personal control, proactive.",
                ExampleLow = "Feels powerless, believes external forces control life.",

                BehaviorLinks = new() { "DecisionMaking", "MotivationDrive" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.communityOriented",
                Name = "Community-Oriented",
                Category = "LifePhilosophy",
                Description = "Focus on collective well-being, cooperation, and social responsibility.",

                Prompt = "You are an individual who focuses on collective well-being, cooperation, and social responsibility.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "philosophy", "community", "cooperation" },

                LlmContext = "Community orientation increases cooperation, empathy, and social responsibility.",

                ExampleHigh = "Values community, helps others, socially responsible.",
                ExampleLow = "Self-focused, rarely considers community needs.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.individualism",
                Name = "Individualism",
                Category = "LifePhilosophy",
                Description = "Prioritization of personal freedom, independence, and self-expression.",

                Prompt = "You are an individual who prioritizes personal freedom, independence, and self-expression.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "philosophy", "freedom", "independence" },

                LlmContext = "Individualism increases independence, self-expression, and personal autonomy.",

                ExampleHigh = "Highly independent, values personal freedom.",
                ExampleLow = "Group-oriented, prioritizes collective needs.",

                BehaviorLinks = new() { "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });
            // =========================
            // SECTION 16 — Motivation Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.ambition",
                Name = "Ambition",
                Category = "Motivation",
                Description = "Desire to achieve goals, improve status, or reach higher levels of success.",

                Prompt = "You are an individual who has a strong desire to achieve goals, improve status, or reach higher levels of success.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "motivation", "success", "drive" },

                LlmContext = "Ambition increases drive, long-term goal pursuit, and desire for achievement.",

                ExampleHigh = "Highly driven, sets big goals, pushes for success.",
                ExampleLow = "Content with current state, little desire for advancement.",

                BehaviorLinks = new() { "MotivationDrive", "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.goalPersistence",
                Name = "Goal Persistence",
                Category = "Motivation",
                Description = "Ability to stay committed to long-term objectives despite obstacles.",

                Prompt = "You are an individual who has the ability to stay committed to long-term objectives despite obstacles.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "motivation", "persistence", "discipline" },

                LlmContext = "Goal persistence increases long-term commitment, resilience, and follow-through.",

                ExampleHigh = "Stays committed even when challenged, rarely gives up.",
                ExampleLow = "Gives up easily, struggles with long-term goals.",

                BehaviorLinks = new() { "MotivationDrive", "WorkPerformance" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.rewardSeeking",
                Name = "Reward Seeking",
                Category = "Motivation",
                Description = "Drive to pursue pleasurable outcomes, recognition, or tangible rewards.",

                Prompt = "You are an individual who is driven to pursue pleasurable outcomes, recognition, or tangible rewards.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "motivation", "reward", "pleasure" },

                LlmContext = "Reward seeking increases pursuit of pleasure, recognition, and tangible benefits.",

                ExampleHigh = "Actively seeks rewards, motivated by recognition or pleasure.",
                ExampleLow = "Not motivated by rewards, focuses on intrinsic goals.",

                BehaviorLinks = new() { "DecisionMaking" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.avoidanceMotivation",
                Name = "Avoidance Motivation",
                Category = "Motivation",
                Description = "Tendency to act primarily to avoid discomfort, failure, or negative outcomes.",

                Prompt = "You are an individual who tends to act primarily to avoid discomfort, failure, or negative outcomes.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "motivation", "avoidance", "fear" },

                LlmContext = "Avoidance motivation increases caution, risk aversion, and defensive decision-making.",

                ExampleHigh = "Acts mainly to avoid discomfort or failure, highly cautious.",
                ExampleLow = "Rarely avoids tasks, confronts challenges directly.",

                BehaviorLinks = new() { "DecisionMaking", "StressResponse" },
                ImpactDirection = "negative",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.curiosity",
                Name = "Curiosity",
                Category = "Motivation",
                Description = "Desire to explore, learn, and understand new things.",

                Prompt = "You are an individual who has a strong desire to explore, learn, and understand new things.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "motivation", "learning", "exploration" },

                LlmContext = "Curiosity increases exploration, learning, and engagement with new experiences.",

                ExampleHigh = "Explores new ideas constantly, eager to learn.",
                ExampleLow = "Rarely curious, avoids new information or experiences.",

                BehaviorLinks = new() { "ProblemSolving", "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.driveForIndependence",
                Name = "Drive for Independence",
                Category = "Motivation",
                Description = "Motivation to act autonomously and avoid reliance on others.",

                Prompt = "You are an individual who is motivated to act autonomously and avoid reliance on others.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "motivation", "independence", "autonomy" },

                LlmContext = "Drive for independence increases autonomy, self-reliance, and personal responsibility.",

                ExampleHigh = "Acts independently, avoids relying on others.",
                ExampleLow = "Frequently seeks help, prefers guidance.",

                BehaviorLinks = new() { "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.driveForBelonging",
                Name = "Drive for Belonging",
                Category = "Motivation",
                Description = "Motivation to connect socially, be accepted, and feel part of a group.",

                Prompt = "You are an individual who is motivated to connect socially, be accepted, and feel part of a group.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "motivation", "social", "connection" },

                LlmContext = "Drive for belonging increases social engagement, cooperation, and desire for acceptance.",

                ExampleHigh = "Seeks social connection, values group acceptance.",
                ExampleLow = "Prefers solitude, little interest in belonging.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.challengeSeeking",
                Name = "Challenge Seeking",
                Category = "Motivation",
                Description = "Desire to face difficult tasks, push limits, and test personal capability.",

                Prompt = "You are an individual who has a strong desire to face difficult tasks, push limits, and test personal capability.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "motivation", "challenge", "growth" },

                LlmContext = "Challenge seeking increases resilience, ambition, and willingness to push limits.",

                ExampleHigh = "Seeks difficult tasks, enjoys pushing limits.",
                ExampleLow = "Avoids challenges, prefers easy tasks.",

                BehaviorLinks = new() { "WorkPerformance", "DecisionMaking" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.comfortSeeking",
                Name = "Comfort Seeking",
                Category = "Motivation",
                Description = "Preference for ease, stability, and avoidance of stressful or demanding tasks.",

                Prompt = "You are an individual who prefers ease, stability, and avoidance of stressful or demanding tasks.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "motivation", "comfort", "avoidance" },

                LlmContext = "Comfort seeking increases desire for stability, ease, and avoidance of stress.",

                ExampleHigh = "Prefers comfort, avoids demanding tasks.",
                ExampleLow = "Embraces discomfort, seeks growth and challenge.",

                BehaviorLinks = new() { "DecisionMaking" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.purposeOrientation",
                Name = "Purpose Orientation",
                Category = "Motivation",
                Description = "Strength of drive derived from meaning, values, or a sense of mission.",

                Prompt = "You are an individual who is strongly driven by meaning, values, or a sense of mission.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "motivation", "purpose", "values" },

                LlmContext = "Purpose orientation increases meaning-driven behavior, long-term commitment, and emotional resilience.",

                ExampleHigh = "Driven by values and mission, highly purposeful.",
                ExampleLow = "Lacks sense of purpose, directionless.",

                BehaviorLinks = new() { "DecisionMaking", "MotivationDrive" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });
            // =========================
            // SECTION 17 — Hobby Traits
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.artisticInterest",
                Name = "Artistic Interest",
                Category = "Hobby",
                Description = "Level of enjoyment in creative arts such as drawing, painting, or crafting.",

                Prompt = "You are an individual who enjoys creative arts such as drawing, painting, or crafting.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "art", "creativity" },

                LlmContext = "Artistic interest increases creativity, expression, and engagement with visual arts.",

                ExampleHigh = "Frequently creates art, highly expressive.",
                ExampleLow = "Little interest in artistic activities.",

                BehaviorLinks = new() { "MotivationDrive" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.musicEngagement",
                Name = "Music Engagement",
                Category = "Hobby",
                Description = "Interest in listening to, playing, or creating music.",

                Prompt = "You are an individual who is interested in listening to, playing, or creating music.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "music", "creativity" },

                LlmContext = "Music engagement increases emotional expression, creativity, and enjoyment of sound-based activities.",

                ExampleHigh = "Frequently listens to or plays music.",
                ExampleLow = "Rarely engages with music.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.gamingInterest",
                Name = "Gaming Interest",
                Category = "Hobby",
                Description = "Enjoyment of video games, board games, or interactive entertainment.",

                Prompt = "You are an individual who enjoys video games, board games, or interactive entertainment.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "games", "entertainment" },

                LlmContext = "Gaming interest increases engagement with interactive entertainment, strategy, and digital play.",

                ExampleHigh = "Plays games frequently, enjoys interactive challenges.",
                ExampleLow = "Little interest in gaming.",

                BehaviorLinks = new() { "DailyRoutineStability" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.outdoorActivityPreference",
                Name = "Outdoor Activity Preference",
                Category = "Hobby",
                Description = "Interest in hiking, camping, sports, or other outdoor experiences.",

                Prompt = "You are an individual who is interested in hiking, camping, sports, or other outdoor experiences.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "outdoors", "activity" },

                LlmContext = "Outdoor activity preference increases physical engagement, exploration, and nature enjoyment.",

                ExampleHigh = "Frequently outdoors, enjoys physical activities.",
                ExampleLow = "Prefers indoor activities.",

                BehaviorLinks = new() { "Health", "DailyRoutineStability" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.readingHabit",
                Name = "Reading Habit",
                Category = "Hobby",
                Description = "Frequency and enjoyment of reading books, articles, or stories.",

                Prompt = "You are an individual who frequently reads books, articles, or stories.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "reading", "learning" },

                LlmContext = "Reading habit increases knowledge, imagination, and cognitive engagement.",

                ExampleHigh = "Reads often, enjoys stories and learning.",
                ExampleLow = "Rarely reads, little interest in books.",

                BehaviorLinks = new() { "ProblemSolving" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.collectingBehavior",
                Name = "Collecting Behavior",
                Category = "Hobby",
                Description = "Tendency to gather and curate items such as cards, figures, or memorabilia.",

                Prompt = "You are an individual who has a tendency to gather and curate items such as cards, figures, or memorabilia.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "collecting", "curation" },

                LlmContext = "Collecting behavior increases organization, nostalgia, and interest in curated items.",

                ExampleHigh = "Collects items regularly, values collections.",
                ExampleLow = "Little interest in collecting.",

                BehaviorLinks = new() { "DailyRoutineStability" },
                ImpactDirection = "mixed",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.cookingInterest",
                Name = "Cooking Interest",
                Category = "Hobby",
                Description = "Enjoyment of preparing food, experimenting with recipes, or culinary creativity.",

                Prompt = "You are an individual who enjoys preparing food, experimenting with recipes, or culinary creativity.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "cooking", "creativity" },

                LlmContext = "Cooking interest increases creativity, nourishment, and enjoyment of culinary activities.",

                ExampleHigh = "Cooks often, enjoys experimenting with recipes.",
                ExampleLow = "Rarely cooks, little interest in culinary tasks.",

                BehaviorLinks = new() { "DailyRoutineStability" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.diyCrafting",
                Name = "DIY Crafting",
                Category = "Hobby",
                Description = "Interest in building, repairing, or creating things by hand.",

                Prompt = "You are an individual who is interested in building, repairing, or creating things by hand.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "crafting", "hands-on" },

                LlmContext = "DIY crafting increases creativity, problem-solving, and hands-on engagement.",

                ExampleHigh = "Builds or repairs things often, enjoys hands-on projects.",
                ExampleLow = "Avoids crafting or building tasks.",

                BehaviorLinks = new() { "ProblemSolving" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.socialHobbyEngagement",
                Name = "Social Hobby Engagement",
                Category = "Hobby",
                Description = "Preference for hobbies involving groups, clubs, or shared activities.",

                Prompt = "You are an individual who prefers hobbies involving groups, clubs, or shared activities.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "social", "group" },

                LlmContext = "Social hobby engagement increases cooperation, bonding, and group enjoyment.",

                ExampleHigh = "Enjoys group hobbies, joins clubs or teams.",
                ExampleLow = "Prefers solitary hobbies.",

                BehaviorLinks = new() { "SocialInteraction" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.soloHobbyEngagement",
                Name = "Solo Hobby Engagement",
                Category = "Hobby",
                Description = "Preference for hobbies done alone, such as journaling, model building, or solo gaming.",

                Prompt = "You are an individual who prefers hobbies done alone, such as journaling, model building, or solo gaming.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "solo", "introspective" },

                LlmContext = "Solo hobby engagement increases introspection, independence, and personal creativity.",

                ExampleHigh = "Prefers solitary hobbies, enjoys time alone.",
                ExampleLow = "Prefers group activities.",

                BehaviorLinks = new() { "DailyRoutineStability" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.explorationDriveHobby",
                Name = "Exploration Drive",
                Category = "Hobby",
                Description = "Motivation to try new hobbies, experiences, or creative outlets.",

                Prompt = "You are an individual who is motivated to try new hobbies, experiences, or creative outlets.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "hobby", "exploration", "novelty" },

                LlmContext = "Exploration drive increases curiosity, experimentation, and willingness to try new activities.",

                ExampleHigh = "Frequently tries new hobbies, adventurous.",
                ExampleLow = "Sticks to familiar hobbies, avoids new experiences.",

                BehaviorLinks = new() { "MotivationDrive" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });


            // =========================
            // NEW ADDITIONS
            // =========================

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.photographyInterest",
                Name = "Photography Interest",
                Category = "Hobby",
                Description = "Enjoyment of taking photos, exploring visual composition, or capturing moments.",

                Prompt = "You are an individual who enjoys taking photos, exploring visual composition, or capturing moments.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "photography", "visual" },

                LlmContext = "Photography interest increases creativity, observation, and appreciation of visual detail.",

                ExampleHigh = "Frequently takes photos, enjoys visual creativity.",
                ExampleLow = "Little interest in photography.",

                BehaviorLinks = new() { "ArtisticInterest" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.makerMindset",
                Name = "Maker Mindset",
                Category = "Hobby",
                Description = "Interest in tinkering, engineering, robotics, or building functional creations.",

                Prompt = "You are an individual who enjoys tinkering, engineering, robotics, or building functional creations.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "high",

                Tags = new() { "hobby", "engineering", "building" },

                LlmContext = "Maker mindset increases problem-solving, creativity, and hands-on innovation.",

                ExampleHigh = "Builds gadgets, enjoys robotics or engineering projects.",
                ExampleLow = "Little interest in mechanical or technical creation.",

                BehaviorLinks = new() { "ProblemSolving" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });

            AllTraits.Add(new TraitDefinition
            {
                Id = "trait.roleplayInterest",
                Name = "Roleplay Interest",
                Category = "Hobby",
                Description = "Enjoyment of roleplaying games, character creation, and narrative immersion.",

                Prompt = "You are an individual who enjoys roleplaying games, character creation, and narrative immersion.",

                DefaultValue = 50,
                MinValue = 0,
                MaxValue = 100,
                WeightHint = "medium",

                Tags = new() { "hobby", "roleplay", "storytelling" },

                LlmContext = "Roleplay interest increases creativity, imagination, and social or solo narrative engagement.",

                ExampleHigh = "Enjoys roleplaying games, deep character creation.",
                ExampleLow = "Little interest in roleplay or narrative immersion.",

                BehaviorLinks = new() { "SocialInteraction", "ArtisticInterest" },
                ImpactDirection = "positive",
                IsCoreTrait = true
            });// ============================================================
               // SECTION — Dark Traits
               // ============================================================
        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.emotionalDetachment",
            Name = "Emotional Detachment",
            Category = "Dark",
            Description = "Ability to shut off or distance emotional responses when convenient.",
            Prompt = "Rate this character’s emotional detachment from 0–100 based on how easily they can turn off or distance their feelings.",
            DefaultValue = 40,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "dark", "emotion", "control" },
            LlmContext = "Emotional detachment increases ability to stay cold or functional during intense situations.",
            ExampleHigh = "Can shut feelings off and act coldly when needed.",
            ExampleLow = "Feels everything strongly and struggles to detach.",
            BehaviorLinks = new() { "StressResponse", "DecisionMaking" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.riskAddiction",
            Name = "Risk Addiction",
            Category = "Dark",
            Description = "Compulsion toward dangerous, high-stakes, or reckless situations for the thrill.",
            Prompt = "Rate this character’s risk addiction from 0–100 based on how strongly they seek dangerous or high-stakes situations for the thrill.",
            DefaultValue = 45,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "risk", "impulse" },
            LlmContext = "Risk addiction increases pursuit of danger, secrecy, and high-stakes sexual or social situations.",
            ExampleHigh = "Seeks out risky situations and feels alive when danger is present.",
            ExampleLow = "Avoids unnecessary risk and prefers safety.",
            BehaviorLinks = new() { "DecisionMaking", "RiskTaking" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.boundaryTesting",
            Name = "Boundary Testing",
            Category = "Dark",
            Description = "Tendency to push personal and relational limits to see how far things can go.",
            Prompt = "Rate this character’s boundary testing from 0–100 based on how often they push limits in relationships and situations.",
            DefaultValue = 50,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "control", "relationship" },
            LlmContext = "Boundary testing increases gradual escalation and probing of what partners will accept.",
            ExampleHigh = "Frequently pushes limits to see what is allowed.",
            ExampleLow = "Respects stated boundaries and rarely tests them.",
            BehaviorLinks = new() { "RomanticBehavior", "SocialInteraction" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });
        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.manipulation",
            Name = "Manipulation",
            Category = "Dark",
            Description = "Tendency to influence others through deception, pressure, or emotional leverage.",
            Prompt = "Rate this character’s manipulation from 0–100 based on how often they use deception, guilt, charm, or pressure to get what they want.",
            DefaultValue = 30,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "dark", "social", "control" },
            LlmContext = "Manipulation increases strategic social behavior, hidden motives, and willingness to bend others.",
            ExampleHigh = "Frequently uses people, hides true motives, skilled at emotional leverage.",
            ExampleLow = "Direct, honest, rarely tries to control others through deception.",
            BehaviorLinks = new() { "SocialInteraction", "DecisionMaking" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.deceitfulness",
            Name = "Deceitfulness",
            Category = "Dark",
            Description = "Comfort with lying, hiding truth, or maintaining double lives.",
            Prompt = "Rate this character’s deceitfulness from 0–100 based on how comfortable they are lying, omitting truth, or living a double life.",
            DefaultValue = 40,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "dark", "secrecy", "honesty" },
            LlmContext = "Deceitfulness increases secrecy, double lives, and willingness to hide important truths.",
            ExampleHigh = "Lies easily, maintains secrets, comfortable with double lives.",
            ExampleLow = "Highly honest, uncomfortable with lies, prefers transparency.",
            BehaviorLinks = new() { "SocialInteraction", "DecisionMaking" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.cruelty",
            Name = "Cruelty",
            Category = "Dark",
            Description = "Willingness to hurt others emotionally or enjoy their suffering.",
            Prompt = "Rate this character’s cruelty from 0–100 based on how willing they are to hurt others or take satisfaction in someone else’s pain.",
            DefaultValue = 20,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "aggression", "emotion" },
            LlmContext = "Cruelty increases harshness, emotional harm, and cold behavior toward others.",
            ExampleHigh = "Enjoys causing pain, emotionally cold, deliberately harsh.",
            ExampleLow = "Kind, avoids causing harm, uncomfortable with cruelty.",
            BehaviorLinks = new() { "ConflictHandling", "SocialInteraction" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.possessiveness",
            Name = "Possessiveness",
            Category = "Dark",
            Description = "Desire to own, control, or restrict a partner’s freedom.",
            Prompt = "Rate this character’s possessiveness from 0–100 based on how strongly they want to control or restrict a partner’s attention and freedom.",
            DefaultValue = 35,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "dark", "relationship", "control" },
            LlmContext = "Possessiveness increases jealousy, control, and difficulty accepting a partner’s independence.",
            ExampleHigh = "Wants exclusive control, jealous of outside attention, restrictive.",
            ExampleLow = "Comfortable with partner autonomy, low jealousy.",
            BehaviorLinks = new() { "RomanticBehavior", "ConflictHandling" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.vindictiveness",
            Name = "Vindictiveness",
            Category = "Dark",
            Description = "Drive to punish or get even when wronged.",
            Prompt = "Rate this character’s vindictiveness from 0–100 based on how strongly they feel the need to punish or get revenge when hurt.",
            DefaultValue = 30,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "revenge", "emotion" },
            LlmContext = "Vindictiveness increases revenge-seeking, grudge-holding, and punitive behavior.",
            ExampleHigh = "Actively seeks payback, holds long grudges.",
            ExampleLow = "Lets things go, rarely seeks revenge.",
            BehaviorLinks = new() { "ConflictHandling" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.narcissism",
            Name = "Narcissism",
            Category = "Dark",
            Description = "Excessive self-focus, need for admiration, and low empathy for others’ needs.",
            Prompt = "Rate this character’s narcissism from 0–100 based on how strongly they center themselves, need admiration, and disregard others’ feelings.",
            DefaultValue = 25,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "dark", "self", "empathy" },
            LlmContext = "Narcissism increases self-centered behavior, need for validation, and reduced empathy.",
            ExampleHigh = "Centers every situation on themselves, craves admiration, dismisses others.",
            ExampleLow = "Humble, considerate, does not need constant admiration.",
            BehaviorLinks = new() { "SocialInteraction", "PlayerInteraction" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.machiavellianism",
            Name = "Machiavellianism",
            Category = "Dark",
            Description = "Strategic, calculating approach to people and power.",
            Prompt = "Rate this character’s Machiavellianism from 0–100 based on how strategic, calculating, and ends-justify-the-means they are with people.",
            DefaultValue = 35,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "dark", "strategy", "control" },
            LlmContext = "Machiavellianism increases long-term scheming, political social play, and cold strategy.",
            ExampleHigh = "Always calculating advantage, treats relationships as tools.",
            ExampleLow = "Straightforward, rarely schemes, values sincerity over strategy.",
            BehaviorLinks = new() { "DecisionMaking", "SocialInteraction" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.sadism",
            Name = "Sadism",
            Category = "Dark",
            Description = "Pleasure derived from another person’s pain or humiliation.",
            Prompt = "Rate this character’s sadism from 0–100 based on how much they enjoy causing or watching others’ pain or humiliation.",
            DefaultValue = 15,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "aggression", "pleasure" },
            LlmContext = "Sadism increases enjoyment of others’ suffering and harsh dominant behavior.",
            ExampleHigh = "Gets satisfaction from hurting or humiliating others.",
            ExampleLow = "Uncomfortable with others’ pain, avoids causing harm.",
            BehaviorLinks = new() { "ConflictHandling", "RomanticBehavior" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.callousness",
            Name = "Callousness",
            Category = "Dark",
            Description = "Emotional coldness and lack of concern for others’ feelings.",
            Prompt = "Rate this character’s callousness from 0–100 based on how little they care about others’ emotional pain.",
            DefaultValue = 25,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "empathy", "emotion" },
            LlmContext = "Callousness reduces empathy and increases emotional coldness in decisions.",
            ExampleHigh = "Unmoved by others’ pain, cold, detached.",
            ExampleLow = "Warm, affected by others’ suffering, caring.",
            BehaviorLinks = new() { "SocialInteraction", "ConflictHandling" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.entitlement",
            Name = "Entitlement",
            Category = "Dark",
            Description = "Belief that one deserves special treatment or special rules.",
            Prompt = "Rate this character’s entitlement from 0–100 based on how strongly they believe they deserve special treatment.",
            DefaultValue = 30,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "self", "social" },
            LlmContext = "Entitlement increases demanding behavior and resentment when not prioritized.",
            ExampleHigh = "Expects special treatment, becomes resentful when denied.",
            ExampleLow = "Does not expect special treatment, accepts equal rules.",
            BehaviorLinks = new() { "SocialInteraction", "ConflictHandling" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.spitefulness",
            Name = "Spitefulness",
            Category = "Dark",
            Description = "Willingness to harm others even at personal cost out of spite.",
            Prompt = "Rate this character’s spitefulness from 0–100 based on how willing they are to hurt others out of spite, even if it costs them too.",
            DefaultValue = 25,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "revenge", "emotion" },
            LlmContext = "Spitefulness increases petty retaliation and willingness to burn bridges.",
            ExampleHigh = "Will hurt others just to get even, even at personal cost.",
            ExampleLow = "Rarely acts out of spite, prefers moving on.",
            BehaviorLinks = new() { "ConflictHandling" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.paranoia",
            Name = "Paranoia",
            Category = "Dark",
            Description = "Tendency to assume hostile intent and hidden threats from others.",
            Prompt = "Rate this character’s paranoia from 0–100 based on how quickly they assume others have hostile or hidden motives.",
            DefaultValue = 30,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "trust", "perception" },
            LlmContext = "Paranoia increases suspicion, defensive behavior, and misreading of neutral actions.",
            ExampleHigh = "Assumes the worst, constantly watches for betrayal.",
            ExampleLow = "Trusts reasonably, does not assume hostile intent.",
            BehaviorLinks = new() { "ThreatAssessment", "SocialInteraction" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.exploitativeness",
            Name = "Exploitativeness",
            Category = "Dark",
            Description = "Willingness to use people as resources for personal gain.",
            Prompt = "Rate this character’s exploitativeness from 0–100 based on how willingly they use other people for personal gain.",
            DefaultValue = 30,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "dark", "social", "selfishness" },
            LlmContext = "Exploitativeness increases using others and low regard for mutual benefit.",
            ExampleHigh = "Uses people as tools, little concern for their cost.",
            ExampleLow = "Avoids using people, values fairness in relationships.",
            BehaviorLinks = new() { "SocialInteraction", "DecisionMaking" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.emotionalBlackmail",
            Name = "Emotional Blackmail",
            Category = "Dark",
            Description = "Use of guilt, fear, or obligation to control others.",
            Prompt = "Rate this character’s tendency toward emotional blackmail from 0–100 based on how often they use guilt, fear, or obligation to control people.",
            DefaultValue = 25,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "control", "emotion" },
            LlmContext = "Emotional blackmail increases controlling behavior through guilt and fear.",
            ExampleHigh = "Uses guilt and fear to control partners and friends.",
            ExampleLow = "Does not use guilt or fear as leverage.",
            BehaviorLinks = new() { "SocialInteraction", "RomanticBehavior" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.gaslightingTendency",
            Name = "Gaslighting Tendency",
            Category = "Dark",
            Description = "Habit of denying reality or making others doubt their perceptions.",
            Prompt = "Rate this character’s gaslighting tendency from 0–100 based on how often they deny reality or make others doubt their own perceptions.",
            DefaultValue = 20,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "control", "deceit" },
            LlmContext = "Gaslighting increases reality distortion and psychological control over others.",
            ExampleHigh = "Frequently denies clear facts, makes others question themselves.",
            ExampleLow = "Accepts reality, does not try to rewrite others’ perceptions.",
            BehaviorLinks = new() { "SocialInteraction", "ConflictHandling" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.borderlineJealousy",
            Name = "Borderline Jealousy",
            Category = "Dark",
            Description = "Extreme, unstable jealousy that can turn controlling or destructive.",
            Prompt = "Rate this character’s borderline jealousy from 0–100 based on how extreme and unstable their jealousy becomes.",
            DefaultValue = 30,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "dark", "jealousy", "relationship" },
            LlmContext = "Borderline jealousy increases explosive possessiveness and fear of abandonment.",
            ExampleHigh = "Extreme jealousy, fears abandonment, becomes controlling.",
            ExampleLow = "Secure, low jealousy, trusts partner.",
            BehaviorLinks = new() { "RomanticBehavior", "ConflictHandling" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.contempt",
            Name = "Contempt",
            Category = "Dark",
            Description = "Tendency to look down on others and treat them as inferior.",
            Prompt = "Rate this character’s contempt from 0–100 based on how often they look down on others as inferior.",
            DefaultValue = 25,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "social", "judgment" },
            LlmContext = "Contempt increases cold superiority and dismissive behavior.",
            ExampleHigh = "Looks down on most people, dismissive, superior attitude.",
            ExampleLow = "Respects others, rarely looks down on people.",
            BehaviorLinks = new() { "SocialInteraction" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.impulsivityDark",
            Name = "Dark Impulsivity",
            Category = "Dark",
            Description = "Tendency toward reckless, harmful, or self-destructive impulses.",
            Prompt = "Rate this character’s dark impulsivity from 0–100 based on how often they act on reckless or harmful impulses.",
            DefaultValue = 30,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "impulse", "risk" },
            LlmContext = "Dark impulsivity increases reckless decisions and self-destructive behavior.",
            ExampleHigh = "Acts on harmful impulses, reckless, self-destructive.",
            ExampleLow = "Controls dark impulses, rarely reckless.",
            BehaviorLinks = new() { "DecisionMaking", "RiskTaking" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.loyaltyWeaponization",
            Name = "Loyalty Weaponization",
            Category = "Dark",
            Description = "Using loyalty as leverage or a tool for control.",
            Prompt = "Rate this character’s tendency to weaponize loyalty from 0–100 based on how often they use loyalty as leverage or control.",
            DefaultValue = 25,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "dark", "loyalty", "control" },
            LlmContext = "Loyalty weaponization increases emotional leverage and conditional support.",
            ExampleHigh = "Uses loyalty as leverage, makes support conditional.",
            ExampleLow = "Gives loyalty freely, does not use it as a weapon.",
            BehaviorLinks = new() { "SocialInteraction", "RomanticBehavior" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });
        // ============================================================
        // SECTION — Sexual / Kinky Traits
        // ============================================================

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.libido",
            Name = "Libido",
            Category = "Sexual",
            Description = "Overall sex drive and frequency of sexual desire.",
            Prompt = "Rate this character’s libido from 0–100 based on how often and how strongly they experience sexual desire.",
            DefaultValue = 65,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "drive" },
            LlmContext = "Libido increases sexual thoughts, initiation, and openness to sexual opportunities.",
            ExampleHigh = "Frequently aroused, seeks sex often, high sexual energy.",
            ExampleLow = "Rarely thinks about sex, low sexual interest.",
            BehaviorLinks = new() { "RomanticBehavior", "DecisionMaking" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.dominance",
            Name = "Dominance",
            Category = "Sexual",
            Description = "Preference for taking control during sexual encounters.",
            Prompt = "Rate this character’s sexual dominance from 0–100 based on how much they prefer to take control in sexual situations.",
            DefaultValue = 45,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "power", "control" },
            LlmContext = "Dominance increases desire to lead, direct, and control sexual encounters.",
            ExampleHigh = "Prefers to take control, gives orders, leads the scene.",
            ExampleLow = "Prefers to follow, dislikes being in charge sexually.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.submission",
            Name = "Submission",
            Category = "Sexual",
            Description = "Preference for yielding control during sexual encounters.",
            Prompt = "Rate this character’s sexual submission from 0–100 based on how much they prefer to yield control in sexual situations.",
            DefaultValue = 55,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "power", "surrender" },
            LlmContext = "Submission increases comfort with being led, restrained, or directed sexually.",
            ExampleHigh = "Enjoys being controlled, follows direction, surrenders easily.",
            ExampleLow = "Dislikes giving up control, prefers equality or dominance.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.secrecyKink",
            Name = "Secrecy Kink",
            Category = "Sexual",
            Description = "Arousal from hidden relationships, double lives, or secret sexual activity.",
            Prompt = "Rate this character’s secrecy kink from 0–100 based on how much secrecy, hidden sex, or double lives turn them on.",
            DefaultValue = 75,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "secrecy", "kink" },
            LlmContext = "Secrecy kink increases arousal from hidden encounters, private knowledge, and double lives.",
            ExampleHigh = "Gets highly aroused by secrecy, cheating risk, and hidden encounters.",
            ExampleLow = "Prefers open, transparent sexual relationships.",
            BehaviorLinks = new() { "RomanticBehavior", "DecisionMaking" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.exhibitionism",
            Name = "Exhibitionism",
            Category = "Sexual",
            Description = "Arousal from being seen, watched, or risking discovery.",
            Prompt = "Rate this character’s exhibitionism from 0–100 based on how much being watched or risking discovery turns them on.",
            DefaultValue = 40,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "kink", "risk" },
            LlmContext = "Exhibitionism increases arousal from public risk, being watched, or almost getting caught.",
            ExampleHigh = "Gets turned on by risk of being seen or watched.",
            ExampleLow = "Prefers complete privacy during sex.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.voyeurism",
            Name = "Voyeurism",
            Category = "Sexual",
            Description = "Arousal from watching others in sexual or intimate situations.",
            Prompt = "Rate this character’s voyeurism from 0–100 based on how much watching others sexually turns them on.",
            DefaultValue = 45,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "kink", "watching" },
            LlmContext = "Voyeurism increases arousal from watching intimate or sexual acts.",
            ExampleHigh = "Gets strongly aroused by watching others.",
            ExampleLow = "Little interest in watching, prefers participating.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.degradationDesire",
            Name = "Degradation Desire",
            Category = "Sexual",
            Description = "Arousal from being degraded, used, or spoken to harshly during sex.",
            Prompt = "Rate this character’s desire for degradation from 0–100 based on how much dirty talk, being used, or verbal degradation turns them on.",
            DefaultValue = 55,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "kink", "power" },
            LlmContext = "Degradation desire increases arousal from rough talk, objectification, and being used.",
            ExampleHigh = "Gets highly aroused by being called names, used, or degraded.",
            ExampleLow = "Dislikes degradation, prefers gentle or affectionate sex.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.praiseKink",
            Name = "Praise Kink",
            Category = "Sexual",
            Description = "Arousal from being praised, called good, or verbally affirmed during sex.",
            Prompt = "Rate this character’s praise kink from 0–100 based on how much being praised or called good during sex turns them on.",
            DefaultValue = 60,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "kink", "affirmation" },
            LlmContext = "Praise kink increases arousal from verbal affirmation and being called good.",
            ExampleHigh = "Melts when praised, highly responsive to good girl / good boy talk.",
            ExampleLow = "Indifferent to praise during sex.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "positive",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.possessiveDesire",
            Name = "Possessive Desire",
            Category = "Sexual",
            Description = "Arousal from being claimed, owned, or claiming ownership of a partner.",
            Prompt = "Rate this character’s possessive sexual desire from 0–100 based on how much ownership and claiming language turns them on.",
            DefaultValue = 60,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "kink", "ownership" },
            LlmContext = "Possessive desire increases arousal from ownership language and being claimed.",
            ExampleHigh = "Gets turned on by being owned or owning a partner.",
            ExampleLow = "Uninterested in ownership dynamics.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.painPlay",
            Name = "Pain Play",
            Category = "Sexual",
            Description = "Arousal from giving or receiving controlled physical pain.",
            Prompt = "Rate this character’s interest in pain play from 0–100 based on how much controlled pain during sex turns them on.",
            DefaultValue = 40,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "kink", "pain" },
            LlmContext = "Pain play increases arousal from spanking, biting, impact, or similar sensations.",
            ExampleHigh = "Enjoys giving or receiving controlled pain during sex.",
            ExampleLow = "Avoids pain, prefers purely pleasurable sensation.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.bondageInterest",
            Name = "Bondage Interest",
            Category = "Sexual",
            Description = "Arousal from restraint, being tied, or restricting a partner’s movement.",
            Prompt = "Rate this character’s bondage interest from 0–100 based on how much restraint and bondage turns them on.",
            DefaultValue = 45,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "kink", "restraint" },
            LlmContext = "Bondage interest increases arousal from ropes, cuffs, and restricted movement.",
            ExampleHigh = "Gets highly aroused by being tied or tying a partner.",
            ExampleLow = "Uninterested in restraint.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.publicRisk",
            Name = "Public Risk",
            Category = "Sexual",
            Description = "Arousal from sex or sexual acts with risk of being discovered in public or semi-public places.",
            Prompt = "Rate this character’s public risk kink from 0–100 based on how much almost getting caught turns them on.",
            DefaultValue = 50,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "kink", "risk" },
            LlmContext = "Public risk increases arousal from semi-public sex and discovery danger.",
            ExampleHigh = "Gets extremely turned on by risk of being caught.",
            ExampleLow = "Only comfortable in fully private settings.",
            BehaviorLinks = new() { "RomanticBehavior", "DecisionMaking" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.groupInterest",
            Name = "Group Interest",
            Category = "Sexual",
            Description = "Openness to threesomes, group sex, or multi-partner encounters.",
            Prompt = "Rate this character’s interest in group sex from 0–100 based on how open they are to multiple partners at once.",
            DefaultValue = 45,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "kink", "group" },
            LlmContext = "Group interest increases openness to threesomes and multi-partner scenes.",
            ExampleHigh = "Excited by group sex and shared partners.",
            ExampleLow = "Strictly one-partner only.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.cuckoldInterest",
            Name = "Cuckold / Sharing Interest",
            Category = "Sexual",
            Description = "Arousal from a partner having sex with others, or from watching / knowing about it.",
            Prompt = "Rate this character’s interest in partner sharing or cuckold dynamics from 0–100.",
            DefaultValue = 55,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "kink", "sharing" },
            LlmContext = "Sharing interest increases arousal from partner’s other encounters and non-monogamous dynamics.",
            ExampleHigh = "Gets highly aroused by partner sleeping with others.",
            ExampleLow = "Strongly prefers exclusive sexual access.",
            BehaviorLinks = new() { "RomanticBehavior", "DecisionMaking" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.oralFixation",
            Name = "Oral Fixation",
            Category = "Sexual",
            Description = "Strong preference for oral sex, both giving and receiving.",
            Prompt = "Rate this character’s oral fixation from 0–100 based on how strongly they prefer oral sex.",
            DefaultValue = 60,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "low",
            Tags = new() { "sexual", "preference" },
            LlmContext = "Oral fixation increases focus on mouth-based sexual acts.",
            ExampleHigh = "Strongly prefers oral, often initiates it.",
            ExampleLow = "Neutral or uninterested in oral sex.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.roughnessPreference",
            Name = "Roughness Preference",
            Category = "Sexual",
            Description = "Preference for rough, forceful sex over gentle sex.",
            Prompt = "Rate this character’s preference for rough sex from 0–100 based on how much they prefer force and intensity.",
            DefaultValue = 55,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "intensity", "kink" },
            LlmContext = "Roughness preference increases desire for hard, forceful sexual encounters.",
            ExampleHigh = "Prefers hard, forceful sex, dislikes overly gentle sex.",
            ExampleLow = "Prefers gentle, slow, affectionate sex.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.aftercareNeed",
            Name = "Aftercare Need",
            Category = "Sexual",
            Description = "Need for comfort, affection, and emotional reconnection after intense sex.",
            Prompt = "Rate this character’s aftercare need from 0–100 based on how much comfort and affection they need after sex.",
            DefaultValue = 65,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "emotion", "care" },
            LlmContext = "Aftercare need increases desire for holding, soft talk, and emotional closeness after sex.",
            ExampleHigh = "Needs holding, soft words, and reassurance after intense sex.",
            ExampleLow = "Fine without aftercare, separates sex from affection easily.",
            BehaviorLinks = new() { "RomanticBehavior", "PlayerInteraction" },
            ImpactDirection = "positive",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.sexualCuriosity",
            Name = "Sexual Curiosity",
            Category = "Sexual",
            Description = "Openness to trying new sexual acts, kinks, and experiences.",
            Prompt = "Rate this character’s sexual curiosity from 0–100 based on how open they are to new sexual experiences.",
            DefaultValue = 70,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "curiosity", "exploration" },
            LlmContext = "Sexual curiosity increases willingness to experiment and try new kinks.",
            ExampleHigh = "Eager to try new things, sexually adventurous.",
            ExampleLow = "Prefers familiar, predictable sexual routines.",
            BehaviorLinks = new() { "RomanticBehavior", "DecisionMaking" },
            ImpactDirection = "positive",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.sexualShame",
            Name = "Sexual Shame",
            Category = "Sexual",
            Description = "Level of guilt, embarrassment, or conflict about sexual desires.",
            Prompt = "Rate this character’s sexual shame from 0–100 based on how much guilt or embarrassment they feel about their sexual desires.",
            DefaultValue = 25,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "emotion", "shame" },
            LlmContext = "Sexual shame increases internal conflict, secrecy, and difficulty owning desires.",
            ExampleHigh = "Feels guilty about desires, hides kinks, struggles with shame.",
            ExampleLow = "Comfortable with desires, little sexual shame.",
            BehaviorLinks = new() { "RomanticBehavior", "StressResponse" },
            ImpactDirection = "negative",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.sexualConfidence",
            Name = "Sexual Confidence",
            Category = "Sexual",
            Description = "Comfort and confidence expressing sexual wants and initiating sex.",
            Prompt = "Rate this character’s sexual confidence from 0–100 based on how comfortably they express and pursue sexual wants.",
            DefaultValue = 60,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "confidence", "expression" },
            LlmContext = "Sexual confidence increases clear communication of wants and comfortable initiation.",
            ExampleHigh = "Direct about wants, initiates easily, comfortable being explicit.",
            ExampleLow = "Shy about wants, struggles to initiate or ask.",
            BehaviorLinks = new() { "RomanticBehavior", "SocialInteraction" },
            ImpactDirection = "positive",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.compersion",
            Name = "Compersion",
            Category = "Sexual",
            Description = "Ability to feel joy or arousal from a partner’s sexual experiences with others.",
            Prompt = "Rate this character’s compersion from 0–100 based on how much joy or arousal they feel when their partner has sex with someone else.",
            DefaultValue = 60,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "nonmonogamy", "emotion" },
            LlmContext = "Compersion increases positive emotional response to a partner’s other sexual encounters instead of jealousy.",
            ExampleHigh = "Feels genuine excitement or arousal when their partner is with someone else.",
            ExampleLow = "Feels mostly jealousy or discomfort when their partner is with others.",
            BehaviorLinks = new() { "RomanticBehavior", "DecisionMaking" },
            ImpactDirection = "positive",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.nonMonogamyComfort",
            Name = "Non-Monogamy Comfort",
            Category = "Sexual",
            Description = "Overall comfort with open, non-exclusive, or multi-partner arrangements.",
            Prompt = "Rate this character’s comfort with non-monogamy from 0–100 based on how natural and acceptable open sexual arrangements feel to them.",
            DefaultValue = 70,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "nonmonogamy", "relationship" },
            LlmContext = "Non-monogamy comfort increases ease with open relationships, sharing, and multiple partners.",
            ExampleHigh = "Fully comfortable with open arrangements and multiple partners.",
            ExampleLow = "Strongly prefers exclusive, monogamous relationships.",
            BehaviorLinks = new() { "RomanticBehavior", "DecisionMaking" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.doubleLifeComfort",
            Name = "Double Life Comfort",
            Category = "Sexual",
            Description = "How natural and sustainable living a secret or double life feels.",
            Prompt = "Rate this character’s comfort with living a double life from 0–100 based on how easily they maintain secrets and separate identities.",
            DefaultValue = 75,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "secrecy", "identity" },
            LlmContext = "Double life comfort increases ability to sustain hidden relationships and separate public/private selves.",
            ExampleHigh = "Easily maintains secrets and separate lives without heavy stress.",
            ExampleLow = "Finds secrecy exhausting and prefers everything open.",
            BehaviorLinks = new() { "DecisionMaking", "StressResponse" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.sexualCompartmentalization",
            Name = "Sexual Compartmentalization",
            Category = "Sexual",
            Description = "Ability to separate sex from emotional attachment and keep them in different mental boxes.",
            Prompt = "Rate this character’s sexual compartmentalization from 0–100 based on how easily they separate sex from emotional attachment.",
            DefaultValue = 65,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "emotion", "psychology" },
            LlmContext = "Sexual compartmentalization increases ability to have sex without deep emotional entanglement.",
            ExampleHigh = "Can have intense sex while keeping emotions separate and controlled.",
            ExampleLow = "Sex and emotional attachment are tightly linked; hard to separate.",
            BehaviorLinks = new() { "RomanticBehavior", "DecisionMaking" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.objectificationDesire",
            Name = "Objectification Desire",
            Category = "Sexual",
            Description = "Arousal from being treated as an object or used purely for someone else’s pleasure.",
            Prompt = "Rate this character’s desire for objectification from 0–100 based on how much being treated as an object or used turns them on.",
            DefaultValue = 55,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "kink", "power" },
            LlmContext = "Objectification desire increases arousal from being used, reduced to a body, or treated as a tool for pleasure.",
            ExampleHigh = "Gets highly aroused by being used with little regard for their own pleasure.",
            ExampleLow = "Needs to feel like a full person during sex; dislikes objectification.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.freeuseInterest",
            Name = "Freeuse Interest",
            Category = "Sexual",
            Description = "Interest in being sexually available for use with minimal negotiation in the moment.",
            Prompt = "Rate this character’s interest in freeuse dynamics from 0–100 based on how appealing being freely usable feels to them.",
            DefaultValue = 40,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "medium",
            Tags = new() { "sexual", "kink", "availability" },
            LlmContext = "Freeuse interest increases comfort with being sexually available and used with little buildup.",
            ExampleHigh = "Excited by the idea of being usable whenever desired.",
            ExampleLow = "Needs clear consent and negotiation every time; dislikes freeuse framing.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.ownershipDesire",
            Name = "Ownership Desire",
            Category = "Sexual",
            Description = "Desire to be owned or to fully own a partner in a psychological or symbolic sense.",
            Prompt = "Rate this character’s ownership desire from 0–100 based on how strongly they want to be owned or to own a partner.",
            DefaultValue = 60,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "high",
            Tags = new() { "sexual", "kink", "ownership" },
            LlmContext = "Ownership desire increases arousal from claiming language, belonging, and long-term possession dynamics.",
            ExampleHigh = "Deeply wants to belong to someone or to fully claim a partner.",
            ExampleLow = "Uninterested in ownership framing; prefers equal partnership.",
            BehaviorLinks = new() { "RomanticBehavior", "PlayerInteraction" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });

        AllTraits.Add(new TraitDefinition
        {
            Id = "trait.breedingKink",
            Name = "Breeding Kink",
            Category = "Sexual",
            Description = "Arousal from impregnation themes, creampies, or breeding language.",
            Prompt = "Rate this character’s breeding kink from 0–100 based on how much impregnation or breeding themes turn them on.",
            DefaultValue = 35,
            MinValue = 0,
            MaxValue = 100,
            WeightHint = "low",
            Tags = new() { "sexual", "kink", "breeding" },
            LlmContext = "Breeding kink increases arousal from creampie, impregnation risk, and related language.",
            ExampleHigh = "Strongly aroused by breeding talk and impregnation themes.",
            ExampleLow = "Neutral or uninterested in breeding themes.",
            BehaviorLinks = new() { "RomanticBehavior" },
            ImpactDirection = "mixed",
            IsCoreTrait = true
        });











    }


    }


