namespace ProjectEve.Money
{
    public class MoneyProfile
    {
        // =====================================================
        // WALLET
        // =====================================================
        public decimal Cash { get; set; } = 120m;   // on hand
        public decimal Bank { get; set; } = 400m;   // reserve
        public decimal Debt { get; set; } = 0m;

        // =====================================================
        // BUDGET TENDENCIES (personality of spending)
        // Keep as relative weights if you want; not required for psy
        // =====================================================
        public decimal Bills { get; set; } = 0.40m;
        public decimal Food { get; set; } = 0.25m;
        public decimal Entertainment { get; set; } = 0.15m;
        public decimal SavingsRate { get; set; } = 0.10m;
        public decimal HobbySpending { get; set; } = 0.10m;

        // =====================================================
        // DERIVED
        // =====================================================
        public decimal Liquid => Cash + Bank;
        public decimal Available => Cash - Debt;

        public string PressureLabel()
        {
            var available = Available;

            if (available < 40m) return "broke";
            if (available < 150m) return "tight";
            if (available < 800m) return "stable";
            return "comfortable";
        }

        /// <summary>
        /// Positive = money stress pushing practical behavior (work/avoid spend).
        /// Negative = ease / less survival pressure.
        /// </summary>
        public int StressBias()
        {
            return PressureLabel() switch
            {
                "broke" => 25,
                "tight" => 12,
                "stable" => 0,
                _ => -5
            };
        }

        /// <summary>
        /// Positive = can fund impulse / desire logistics.
        /// Negative = desire still exists but money blocks the easy path.
        /// </summary>
        public int DesireFundingBias()
        {
            return PressureLabel() switch
            {
                "broke" => -10,
                "tight" => -3,
                "stable" => 4,
                _ => 10
            };
        }

        public void AdjustCash(decimal amount)
        {
            Cash += amount;
            if (Cash < 0m) Cash = 0m;
        }

        public void AdjustBank(decimal amount)
        {
            Bank += amount;
            if (Bank < 0m) Bank = 0m;
        }
    }
}