namespace CoreBanking.Api.Data;

public static class WorkshopSeed
{
    public static readonly string[] Types = ["CURRENT_ACCOUNT", "REGULAR_SAVINGS", "FIXED_DEPOSIT", "SAVINGS_PLAN", "INVESTOR_ACCOUNT"];
    public static readonly string[] States = ["PENDING_APPROVAL", "APPROVED", "ACTIVE", "ACTIVE_IN_ARREARS", "MATURED", "LOCKED", "DORMANT", "CLOSED", "CLOSED_WRITTEN_OFF", "WITHDRAWN", "CLOSED_REJECTED"];
    public static readonly string[] ProductIds = ["mock-product-current-eur", "mock-product-savings-eur", "mock-product-fixed-eur", "mock-product-savings-plan-eur", "mock-product-investor-eur"];
    public static readonly DateTimeOffset Cutoff = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    public static Customer[] Customers() => Enumerable.Range(100001, 1000)
        .Select(number => new Customer { Id = $"cus-{number}", HolderKey = $"mock-client-{number}" }).ToArray();

    public static ProductMapping[] Products() => Enumerable.Range(0, 5)
        .Select(index => new ProductMapping { Id = ProductIds[index], AccountType = Types[index] }).ToArray();

    public static Account[] Accounts()
    {
        var fixedStates = new Dictionary<int, int>
        {
            [50001] = 2, [50002] = 7, [50003] = 2, [50004] = 2,
            [50005] = 2, [50006] = 2, [50007] = 2, [50008] = 7,
            [50009] = 2, [50010] = 2, [50011] = 1,
            [50677] = 2, [50678] = 5, [50679] = 0, [50680] = 2
        };
        var remaining = Enumerable.Range(0, 11).Select(index => index < 7 ? 364 : 363).ToArray();
        foreach (var state in fixedStates.Values) remaining[state]--;
        var result = new List<Account>();
        for (var number = 50001; number <= 54000; number++)
        {
            var index = number - 50001;
            var stateIndex = fixedStates.TryGetValue(number, out var reserved)
                ? reserved : Array.FindIndex(remaining, count => count > 0);
            if (!fixedStates.ContainsKey(number)) remaining[stateIndex]--;
            var book = stateIndex is 0 or 9 or 10 ? 0m : (index % 100 + 1) * 100m + 0.25m;
            var holds = book > 0 && index % 3 == 0 ? 25m : 0m;
            var account = new Account
            {
                Id = $"acc-{number}", CustomerId = $"cus-{100001 + index % 1000}",
                ProductId = ProductIds[index % 5], AccountType = Types[index % 5],
                Name = $"Workshop account {number}", AccountState = States[stateIndex],
                BookBalance = book, AvailableBalance = book - holds, Holds = holds,
                AsOf = Cutoff
            };
            if (stateIndex == 3 || stateIndex == 2 && index % 13 == 0)
            {
                account.BookBalance = -100m;
                account.AvailableBalance = -100m;
                account.Holds = 0;
                account.OverdraftLimit = 500m;
            }
            if (fixedStates.ContainsKey(number)) account.CustomerId = "cus-100045";
            switch (number)
            {
                case 50001: account.BookBalance = 5650.25m; account.AvailableBalance = 5400.25m; account.Holds = 250m; break;
                case 50002: account.BookBalance = 0; account.AvailableBalance = 0; account.Holds = 0; break;
                case 50003: case 50011: account.BookBalance = 0; account.AvailableBalance = 0; account.Holds = 0; break;
                case 50004: account.BookBalance = 10; account.AvailableBalance = 10; account.Holds = 0; account.OverdraftLimit = 0; break;
                case 50005: account.BookBalance = -100; account.AvailableBalance = -100; account.Holds = 0; account.OverdraftLimit = 500; break;
                case 50006: account.BookBalance = null; break;
                case 50007: account.AvailableBalance = null; account.BookBalance = null; break;
                case 50008: account.AvailableBalance = null; break;
                case 50009: account.BookBalance = 1000; account.AvailableBalance = 1000; account.Holds = 0; break;
                case 50010: account.BookBalance = 100; account.AvailableBalance = 50; account.Holds = 50; break;
                case 50677: account.BalanceInquiryBlocked = true; break;
                case 50680: account.AvailableBalance = null; break;
            }
            result.Add(account);
        }
        return result.ToArray();
    }
}