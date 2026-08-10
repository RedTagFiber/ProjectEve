namespace ProjectEve.Money
{
    /// <summary>
    /// Wallet + soft budget weights. Feeds Psy via StressBias / DesireFundingBias.
    /// </summary>
    public class MoneyProfile
    {
        // =====================================================
        // WALLET
        // =====================================================
        public decimal Cash { get; set; } = 120m;
        public decimal Bank { get; set; } = 400m;
        public decimal Debt { get; set; } = 0m;

        // =====================================================
        // BUDGET TENDENCIES (relative weights, sum ~1.0)
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

        /// <summary>Cash after debt pressure (can go negative conceptually; we clamp displays).</summary>
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
        /// Positive = money stress → work / avoid spend.
        /// Negative = ease.
        /// </summary>
        public int StressBias() => PressureLabel() switch
        {
            "broke" => 25,
            "tight" => 12,
            "stable" => 0,
            _ => -5
        };

        /// <summary>
        /// Positive = can fund impulse / date / ticket.
        /// Negative = desire exists but cash blocks the easy path.
        /// </summary>
        public int DesireFundingBias() => PressureLabel() switch
        {
            "broke" => -10,
            "tight" => -3,
            "stable" => 4,
            _ => 10
        };

        // =====================================================
        // MUTATIONS
        // =====================================================
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

        public void AdjustDebt(decimal amount)
        {
            Debt += amount;
            if (Debt < 0m) Debt = 0m;
        }

        /// <summary>Spend from cash if possible. Returns false if short.</summary>
        public bool TrySpendCash(decimal amount)
        {
            if (amount <= 0m) return true;
            if (Cash < amount) return false;
            Cash -= amount;
            return true;
        }

        /// <summary>Spend cash first, then bank. Returns false if still short.</summary>
        public bool TrySpend(decimal amount)
        {
            if (amount <= 0m) return true;
            if (Liquid < amount) return false;

            if (Cash >= amount)
            {
                Cash -= amount;
                return true;
            }

            amount -= Cash;
            Cash = 0m;
            Bank -= amount;
            if (Bank < 0m) Bank = 0m;
            return true;
        }

        public void DepositToBank(decimal amount)
        {
            if (amount <= 0m) return;
            if (Cash < amount) amount = Cash;
            Cash -= amount;
            Bank += amount;
        }

        public void WithdrawToCash(decimal amount)
        {
            if (amount <= 0m) return;
            if (Bank < amount) amount = Bank;
            Bank -= amount;
            Cash += amount;
        }

        /// <summary>Pay debt from liquid; leftover stays debt.</summary>
        public decimal PayDebt(decimal amount)
        {
            if (amount <= 0m || Debt <= 0m) return 0m;
            if (amount > Debt) amount = Debt;
            if (!TrySpend(amount))
            {
                // pay what we can
                amount = Liquid;
                if (amount <= 0m) return 0m;
                TrySpend(amount);
            }
            Debt -= amount;
            if (Debt < 0m) Debt = 0m;
            return amount;
        }

        /// <summary>Paycheck / gift / refund into bank by default.</summary>
        public void Receive(decimal amount, bool toCash = false)
        {
            if (amount <= 0m) return;
            if (toCash) Cash += amount;
            else Bank += amount;
        }

        public override string ToString()
            => $"Cash {Cash:0.00} | Bank {Bank:0.00} | Debt {Debt:0.00} | {PressureLabel()}";
    }
}