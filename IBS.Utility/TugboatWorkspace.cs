namespace IBS.Utility
{
    public sealed class TugboatWorkspace
    {
        public const string FilterName = "TugboatWorkspace";
        public const string BalanceFilterName = "BalanceWorkspace";
        public const string CookieName = "ibs-workspace";
        public const string FormFieldName = "__Workspace";

        public int? TugboatId { get; private set; }

        public string Name { get; private set; } = "MMSI / All tugboats";

        public void Select(int? tugboatId, string name)
        {
            if (tugboatId is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tugboatId));
            }

            TugboatId = tugboatId;
            Name = name;
        }
    }
}
