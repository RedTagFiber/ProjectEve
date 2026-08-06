namespace ProjectEve.Characters.Emotion
{
    public enum EmotionState
    {
        // baseline
        Neutral,
        Content,
        Calm,
        Soft,
        Focused,
        Tired,
        Numb,

        // positive
        Happy,
        Excited,
        Playful,
        Affectionate,
        InLove,

        // stress / pain
        Sad,
        Lonely,
        Anxious,
        Uneasy,
        Stressed,
        Overwhelmed,
        Scared,
        Embarrassed,
        Guilty,
        Jealous,
        Irritated,
        Angry,

        // desire
        Horny,
        Hungry,          // restless wanting, not only sexual
        Tempted,         // knows better, still pulled
        Reckless,        // wants the risky choice
        Cruel,           // wants to hurt a little
        Spiteful,        // wants to get even
        Vindictive,      // longer cold payback mood
        Predatory,       // hunting / taking energy
        Possessive,      // mine, don't touch
        Obsessive,       // can't stop circling one person/thing
        Ashamed,         // darker than embarrassed
        Corrupted,       // leaning into the bad urge on purpose
        Detached,        // shut off, can do cold things easily
        Manic,           // too high, bad decisions feel fun
        Hollow,          // empty enough to seek damage/noise
        Restless,        // needs movement, trouble, stimulation
        Bitter,          // old resentment active
        Smug,            // enjoyed getting away with something
        GuiltyPleasure,  // likes the wrong thing and knows it
        SelfDestructive  // wants to mess self up / burn it down
    }
}