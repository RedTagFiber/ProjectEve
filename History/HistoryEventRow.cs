namespace ProjectEve.History
{
    public class HistoryEventRow
    {
        public string EventId { get; set; } = "";
        public string? ArcId { get; set; }
        public string? ParentEventId { get; set; }
        public string? SeasonId { get; set; }
        public string WorldId { get; set; } = "ohio";

        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string? PlaceText { get; set; }
        public string? LocationId { get; set; }

        public string ChannelMix { get; set; } = "text";
        public string Status { get; set; } = "closed";

        public string GameAt { get; set; } = "";
        public string? GameAtEnd { get; set; }
        public string RealAt { get; set; } = "";
        public string? RealAtEnd { get; set; }

        public string ContentRating { get; set; } = "pg";
        public bool HiddenFromPacket { get; set; }
        public string Source { get; set; } = "live_play";
        public int Confidence { get; set; } = 7;

        public int? Fatigue { get; set; }
        public int? Alcohol { get; set; }
        public string? Illness { get; set; }

        public int TurnCount { get; set; }
        public string? LastRecalledAt { get; set; }
        public int RecallCount { get; set; }

        public List<string> Tags { get; set; } = new();
        public List<HistoryParticipantRow> Participants { get; set; } = new();
        public List<HistoryFactRow> Facts { get; set; } = new();
        public List<HistoryPeakRow> Peaks { get; set; } = new();
        public List<HistoryBeatRow> Beats { get; set; } = new();
        public List<string> Aliases { get; set; } = new();
    }

    public class HistoryParticipantRow
    {
        public string EventId { get; set; } = "";
        public int CharacterId { get; set; }
        public string Role { get; set; } = "present";
        public string? RelationshipBand { get; set; }
        public double? LikeScoreAtTime { get; set; }
    }

    public class HistoryFactRow
    {
        public int FactId { get; set; }
        public string EventId { get; set; } = "";
        public string Kind { get; set; } = "detail";
        public string Text { get; set; } = "";
        public bool Locked { get; set; }
        public string? PromiseStatus { get; set; }
        public string? DueGameAt { get; set; }
    }

    public class HistoryPeakRow
    {
        public int PeakId { get; set; }
        public string EventId { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Text { get; set; } = "";
        public int Intensity { get; set; } = 5;
        public bool Locked { get; set; }
        public string? PhotoPath { get; set; }
        public string? CutsceneId { get; set; }
        public string? VoicePath { get; set; }
    }

    public class HistoryBeatRow
    {
        public int BeatId { get; set; }
        public string EventId { get; set; } = "";
        public string? GameAt { get; set; }
        public string? SpeakerPlayer { get; set; }
        public string? SpeakerNpc { get; set; }
        public int Importance { get; set; } = 5;
        public string? Kind { get; set; }
    }
}