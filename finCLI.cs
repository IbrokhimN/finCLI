using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public class Program
{
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("Personal Finance Manager v1.0");
        Console.WriteLine("Type 'help' for commands or 'menu' for menu\n");
        
        var manager = new FinanceManager();
        var cli = new CLI(manager);
        
        if (args.Length > 0)
        {
            cli.ExecuteCommand(args);
        }
        else
        {
            cli.InteractiveMode();
        }
    }
}

public class Transaction
{
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = "Other";
    public string Note { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    
    public bool IsIncome => Amount > 0;
    
    public string FormattedAmount => 
        $"{(IsIncome ? "+" : "-")} {Math.Abs(Amount):N2}";
}

public class FinanceManager
{
    private List<Transaction> _transactions = new List<Transaction>();
    private Dictionary<string, string> _categoryRules = new Dictionary<string, string>();
    private decimal _monthlyBudget = 0;
    private string _dataFile = "finance_data.json";
    private string _backupFile = "finance_data_backup.json";
    
    private Dictionary<string, object> _reportCache = new Dictionary<string, object>();
    private bool _isDataDirty = true;

    public FinanceManager()
    {
        LoadData();
        InitializeDefaultCategories();
    }
    
    private void InitializeDefaultCategories()
    {
        if (_categoryRules.Count == 0)
        {
            _categoryRules = new Dictionary<string, string>
            {
                { @"(?i)(food|grocery|supermarket|cafe|restaurant|lunch|dinner|breakfast)", "Food" },
                { @"(?i)(transport|taxi|bus|metro|gas|fuel)", "Transport" },
                { @"(?i)(utilities|electricity|water|gas|internet)", "Utilities" },
                { @"(?i)(rent|mortgage|housing)", "Rent" },
                { @"(?i)(health|doctor|pharmacy|hospital|medicine)", "Health" },
                { @"(?i)(entertainment|movie|theater|concert|hobby)", "Entertainment" },
                { @"(?i)(clothes|shoes|shopping)", "Clothing" },
                { @"(?i)(salary|income|deposit|transfer)", "Income" },
                { @"(?i)(gift|bonus|reward)", "Bonus" }
            };
        }
    }
    
    public void AddTransaction(Transaction transaction)
    {
        if (string.IsNullOrEmpty(transaction.Category) || transaction.Category == "Other")
        {
            transaction.Category = CategorizeTransaction(transaction.Note);
        }
        
        _transactions.Add(transaction);
        _isDataDirty = true;
        _transactions = _transactions.OrderBy(t => t.Date).ToList();
        
        Console.WriteLine($"Added: {transaction.FormattedAmount} [{transaction.Category}]");
        
        if (_monthlyBudget > 0)
        {
            CheckBudgetWarning(transaction.Date);
        }
        
        SaveData();
    }
    
    private string CategorizeTransaction(string note)
    {
        if (string.IsNullOrEmpty(note)) return "Other";
        
        foreach (var rule in _categoryRules)
        {
            if (Regex.IsMatch(note, rule.Key))
            {
                return rule.Value;
            }
        }
        
        return "Other";
    }
    
    private void CheckBudgetWarning(DateTime date)
    {
        var monthExpenses = GetMonthlyExpenses(date.Year, date.Month);
        var budgetUsage = monthExpenses / _monthlyBudget;
        
        if (budgetUsage > 0.9m)
        {
            Console.WriteLine($"Warning: {budgetUsage:P0} of budget used in {date:MMMM}");
        }
    }
    
    private decimal GetMonthlyExpenses(int year, int month)
    {
        return _transactions
            .Where(t => t.Date.Year == year && t.Date.Month == month && !t.IsIncome)
            .Sum(t => Math.Abs(t.Amount));
    }
    
    public void ImportFromCsv(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}");
            return;
        }
        
        try
        {
            var lines = File.ReadAllLines(filePath);
            int importedCount = 0;
            
            for (int i = 1; i < lines.Length; i++)
            {
                var fields = ParseCsvLine(lines[i]);
                
                if (fields.Length >= 3)
                {
                    var transaction = new Transaction
                    {
                        Date = DateTime.Parse(fields[0]),
                        Amount = decimal.Parse(fields[1], CultureInfo.InvariantCulture),
                        Note = fields[2],
                        Category = fields.Length > 3 ? fields[3] : "Other"
                    };
                    
                    AddTransaction(transaction);
                    importedCount++;
                }
            }
            
            Console.WriteLine($"Imported {importedCount} transactions from {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Import error: {ex.Message}");
        }
    }
    
    private string[] ParseCsvLine(string line)
    {
        return line.Split(',').Select(f => f.Trim('"').Trim()).ToArray();
    }
    
    public void GenerateReport(string period = "month")
    {
        if (_isDataDirty)
        {
            _reportCache.Clear();
        }
        
        var cacheKey = $"report_{period}";
        if (_reportCache.ContainsKey(cacheKey))
        {
            DisplayCachedReport(cacheKey);
            return;
        }
        
        switch (period.ToLower())
        {
            case "month":
                GenerateMonthlyReport();
                break;
            case "year":
                GenerateYearlyReport();
                break;
            case "all":
                GenerateAllTimeReport();
                break;
            default:
                Console.WriteLine("Unknown period. Use: month, year or all");
                break;
        }
    }
    
    private void GenerateMonthlyReport()
    {
        var now = DateTime.Now;
        var monthTransactions = _transactions
            .Where(t => t.Date.Year == now.Year && t.Date.Month == now.Month)
            .ToList();
        
        if (!monthTransactions.Any())
        {
            Console.WriteLine("No data for current month");
            return;
        }
        
        var income = monthTransactions.Where(t => t.IsIncome).Sum(t => t.Amount);
        var expenses = monthTransactions.Where(t => !t.IsIncome).Sum(t => Math.Abs(t.Amount));
        var balance = income - expenses;
        
        var categories = monthTransactions
            .GroupBy(t => t.Category)
            .Select(g => new { Category = g.Key, Amount = g.Sum(t => Math.Abs(t.Amount)) })
            .OrderByDescending(x => x.Amount)
            .ToList();
        
        _reportCache["report_month"] = new { 
            Period = $"{now:MMMM yyyy}", 
            Income = income, 
            Expenses = expenses, 
            Balance = balance,
            Categories = categories 
        };
        
        DisplayMonthlyReport();
    }
    
    private void DisplayMonthlyReport()
    {
        var cache = _reportCache["report_month"] as dynamic;
        if (cache == null) return;
        
        Console.WriteLine($"\nReport for {cache.Period}:");
        Console.WriteLine("".PadRight(40, '-'));
        
        decimal maxAmount = cache.Categories.Count > 0 ? 
            ((List<dynamic>)cache.Categories).Max(x => (decimal)x.Amount) : 0;
        
        foreach (var category in cache.Categories)
        {
            var barLength = maxAmount > 0 ? (int)((decimal)category.Amount / maxAmount * 20) : 0;
            var bar = "".PadRight(barLength, '#');
            Console.WriteLine($"{category.Category,-12} {bar,-20} {category.Amount,10:N0}");
        }
        
        Console.WriteLine("".PadRight(40, '-'));
        Console.WriteLine($"Income:      {cache.Income,10:N0}");
        Console.WriteLine($"Expenses:    {cache.Expenses,10:N0}");
        Console.WriteLine($"Balance:     {cache.Balance,10:N0}");
        
        if (_monthlyBudget > 0)
        {
            var usage = cache.Expenses / _monthlyBudget;
            var budgetBarLength = (int)(usage * 20);
            var budgetBar = "".PadRight(Math.Min(budgetBarLength, 20), '#');
            
            Console.WriteLine($"\nBudget: {cache.Expenses:N0} / {_monthlyBudget:N0}");
            Console.WriteLine($"[{budgetBar,-20}] {usage:P0}");
            
            if (usage > 1m)
            {
                Console.WriteLine("BUDGET EXCEEDED!");
            }
        }
    }
    
    private void GenerateYearlyReport()
    {
        var now = DateTime.Now;
        var yearTransactions = _transactions
            .Where(t => t.Date.Year == now.Year)
            .ToList();
        
        if (!yearTransactions.Any())
        {
            Console.WriteLine("No data for current year");
            return;
        }
        
        var monthlyData = yearTransactions
            .GroupBy(t => t.Date.Month)
            .Select(g => new {
                Month = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(g.Key),
                Income = g.Where(t => t.IsIncome).Sum(t => t.Amount),
                Expenses = g.Where(t => !t.IsIncome).Sum(t => Math.Abs(t.Amount))
            })
            .OrderBy(x => Array.IndexOf(CultureInfo.CurrentCulture.DateTimeFormat.MonthNames, x.Month))
            .ToList();
        
        Console.WriteLine($"\nYearly report for {now.Year}:");
        Console.WriteLine("".PadRight(50, '-'));
        Console.WriteLine($"{"Month",-12} {"Income",10} {"Expenses",10} {"Balance",10}");
        Console.WriteLine("".PadRight(50, '-'));
        
        foreach (var month in monthlyData)
        {
            var balance = month.Income - month.Expenses;
            Console.WriteLine($"{month.Month,-12} {month.Income,10:N0} {month.Expenses,10:N0} {balance,10:N0}");
        }
        
        var totalIncome = monthlyData.Sum(m => m.Income);
        var totalExpenses = monthlyData.Sum(m => m.Expenses);
        Console.WriteLine("".PadRight(50, '-'));
        Console.WriteLine($"{"Total",-12} {totalIncome,10:N0} {totalExpenses,10:N0} {totalIncome - totalExpenses,10:N0}");
    }
    
    private void GenerateAllTimeReport()
    {
        if (!_transactions.Any())
        {
            Console.WriteLine("No data available");
            return;
        }
        
        var firstDate = _transactions.Min(t => t.Date);
        var lastDate = _transactions.Max(t => t.Date);
        var totalIncome = _transactions.Where(t => t.IsIncome).Sum(t => t.Amount);
        var totalExpenses = _transactions.Where(t => !t.IsIncome).Sum(t => Math.Abs(t.Amount));
        var avgMonthlyIncome = _transactions
            .Where(t => t.IsIncome)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Average(g => g.Sum(t => t.Amount));
        var avgMonthlyExpenses = _transactions
            .Where(t => !t.IsIncome)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Average(g => g.Sum(t => Math.Abs(t.Amount)));
        
        Console.WriteLine($"\nAll-time statistics:");
        Console.WriteLine($"Period: {firstDate:dd.MM.yyyy} - {lastDate:dd.MM.yyyy}");
        Console.WriteLine($"Total transactions: {_transactions.Count}");
        Console.WriteLine($"Total income: {totalIncome:N0}");
        Console.WriteLine($"Total expenses: {totalExpenses:N0}");
        Console.WriteLine($"Net balance: {totalIncome - totalExpenses:N0}");
        Console.WriteLine($"Avg monthly income: {avgMonthlyIncome:N0}");
        Console.WriteLine($"Avg monthly expenses: {avgMonthlyExpenses:N0}");
        
        var topExpenseCategories = _transactions
            .Where(t => !t.IsIncome)
            .GroupBy(t => t.Category)
            .Select(g => new { Category = g.Key, Amount = g.Sum(t => Math.Abs(t.Amount)) })
            .OrderByDescending(x => x.Amount)
            .Take(5)
            .ToList();
        
        Console.WriteLine($"\nTop 5 expense categories:");
        foreach (var category in topExpenseCategories)
        {
            Console.WriteLine($"  {category.Category,-12} {category.Amount,10:N0}");
        }
    }
    
    private void DisplayCachedReport(string cacheKey)
    {
        if (cacheKey == "report_month")
        {
            DisplayMonthlyReport();
        }
    }
    
    public void ForecastExpenses(int monthsAhead = 1)
    {
        if (!_transactions.Any())
        {
            Console.WriteLine("Not enough data for forecast");
            return;
        }
        
        var cutoffDate = DateTime.Now.AddMonths(-6);
        var recentData = _transactions
            .Where(t => !t.IsIncome && t.Date >= cutoffDate)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new { 
                Period = new DateTime(g.Key.Year, g.Key.Month, 1),
                Expenses = g.Sum(t => Math.Abs(t.Amount))
            })
            .OrderBy(x => x.Period)
            .ToList();
        
        if (recentData.Count < 2)
        {
            Console.WriteLine("Need at least 2 months of data for forecast");
            return;
        }
        
        var forecast = CalculateMovingAverage(recentData.Select(x => x.Expenses).ToArray(), 3);
        
        for (int i = 0; i < monthsAhead; i++)
        {
            var forecastDate = DateTime.Now.AddMonths(i + 1);
            Console.WriteLine($"Forecast for {forecastDate:MMMM yyyy}: ~{forecast:N0}");
        }
    }
    
    private decimal CalculateMovingAverage(decimal[] data, int window)
    {
        if (data.Length == 0) return 0;
        
        var actualWindow = Math.Min(window, data.Length);
        var recentData = data.TakeLast(actualWindow).ToArray();
        
        return recentData.Average();
    }
    
    public void SetBudget(decimal amount)
    {
        _monthlyBudget = amount;
        _isDataDirty = true;
        SaveData();
        Console.WriteLine($"Monthly budget set: {amount:N0}");
    }
    
    public void ListTags()
    {
        var allTags = _transactions
            .SelectMany(t => t.Tags)
            .Where(tag => !string.IsNullOrEmpty(tag))
            .Distinct()
            .ToList();
        
        if (!allTags.Any())
        {
            Console.WriteLine("No tags used");
            return;
        }
        
        Console.WriteLine("Used tags:");
        foreach (var tag in allTags)
        {
            var tagExpenses = _transactions
                .Where(t => t.Tags.Contains(tag) && !t.IsIncome)
                .Sum(t => Math.Abs(t.Amount));
            Console.WriteLine($"  {tag} (expenses: {tagExpenses:N0})");
        }
    }
    
    // Transaction management methods
    public void EditTransaction(int index, Transaction newTransaction)
    {
        if (index < 0 || index >= _transactions.Count)
        {
            Console.WriteLine("Invalid transaction index");
            return;
        }
        
        _transactions[index] = newTransaction;
        _isDataDirty = true;
        SaveData();
        Console.WriteLine("Transaction updated");
    }
    
    public void DeleteTransaction(int index)
    {
        if (index < 0 || index >= _transactions.Count)
        {
            Console.WriteLine("Invalid transaction index");
            return;
        }
        
        var transaction = _transactions[index];
        _transactions.RemoveAt(index);
        _isDataDirty = true;
        SaveData();
        Console.WriteLine($"Deleted: {transaction.FormattedAmount} [{transaction.Category}]");
    }
    
    public List<Transaction> SearchTransactions(string searchTerm)
    {
        return _transactions
            .Where(t => t.Note.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                       t.Category.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
    
    public void ExportToCsv(string filePath)
    {
        try
        {
            var csv = new StringBuilder();
            csv.AppendLine("Date,Amount,Category,Note,Tags");
            
            foreach (var transaction in _transactions)
            {
                var tags = string.Join(";", transaction.Tags);
                csv.AppendLine($"\"{transaction.Date:yyyy-MM-dd}\",{transaction.Amount},\"{transaction.Category}\",\"{transaction.Note}\",\"{tags}\"");
            }
            
            File.WriteAllText(filePath, csv.ToString());
            Console.WriteLine($"Exported {_transactions.Count} transactions to {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Export error: {ex.Message}");
        }
    }
    
    // Data analysis methods
    public void AnalyzeSpendingPatterns()
    {
        if (!_transactions.Any())
        {
            Console.WriteLine("No data for analysis");
            return;
        }
        
        var expenses = _transactions.Where(t => !t.IsIncome).ToList();
        
        // Daily pattern
        var dailyPattern = expenses
            .GroupBy(t => t.Date.DayOfWeek)
            .Select(g => new { Day = g.Key, Amount = g.Sum(t => Math.Abs(t.Amount)) })
            .OrderByDescending(x => x.Amount)
            .ToList();
        
        Console.WriteLine("\nSpending by day of week:");
        foreach (var day in dailyPattern)
        {
            Console.WriteLine($"  {day.Day}: {day.Amount:N0}");
        }
        
        // Monthly trend
        var monthlyTrend = expenses
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new { 
                Period = new DateTime(g.Key.Year, g.Key.Month, 1),
                Amount = g.Sum(t => Math.Abs(t.Amount))
            })
            .OrderBy(x => x.Period)
            .ToList();
        
        Console.WriteLine("\nMonthly spending trend:");
        foreach (var month in monthlyTrend)
        {
            Console.WriteLine($"  {month.Period:MMM yyyy}: {month.Amount:N0}");
        }
    }
    
    public void SetSavingsGoal(string goalName, decimal targetAmount, DateTime targetDate)
    {
        // TODO: implement savings goals
        Console.WriteLine("Savings goals feature coming soon");
    }
    
    public void AddCategoryRule(string pattern, string category)
    {
        _categoryRules[pattern] = category;
        _isDataDirty = true;
        SaveData();
        Console.WriteLine($"Added category rule: {pattern} -> {category}");
    }
    
    public void ListCategoryRules()
    {
        Console.WriteLine("Category rules:");
        foreach (var rule in _categoryRules)
        {
            Console.WriteLine($"  {rule.Key} -> {rule.Value}");
        }
    }
    
    // Data persistence
    private void SaveData()
    {
        try
        {
            var data = new {
                Transactions = _transactions,
                CategoryRules = _categoryRules,
                MonthlyBudget = _monthlyBudget,
                LastSaved = DateTime.Now
            };
            
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, json);
            
            if (File.Exists(_dataFile))
            {
                File.Copy(_dataFile, _backupFile, true);
            }
            
            File.Copy(tempFile, _dataFile, true);
            File.Delete(tempFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Save error: {ex.Message}");
        }
    }
    
    private void LoadData()
    {
        try
        {
            if (File.Exists(_dataFile))
            {
                var json = File.ReadAllText(_dataFile);
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (data.ContainsKey("Transactions"))
                {
                    var transactionsJson = data["Transactions"].ToString();
                    _transactions = JsonSerializer.Deserialize<List<Transaction>>(transactionsJson) ?? new List<Transaction>();
                }
                
                if (data.ContainsKey("CategoryRules"))
                {
                    var rulesJson = data["CategoryRules"].ToString();
                    _categoryRules = JsonSerializer.Deserialize<Dictionary<string, string>>(rulesJson) ?? new Dictionary<string, string>();
                }
                
                if (data.ContainsKey("MonthlyBudget"))
                {
                    _monthlyBudget = decimal.Parse(data["MonthlyBudget"].ToString());
                }
                
                Console.WriteLine($"Loaded {_transactions.Count} transactions");
            }
            else
            {
                Console.WriteLine("No data file found, starting fresh");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Load error: {ex.Message}");
            LoadFromBackup();
        }
    }
    
    private void LoadFromBackup()
    {
        try
        {
            if (File.Exists(_backupFile))
            {
                var json = File.ReadAllText(_backupFile);
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (data.ContainsKey("Transactions"))
                {
                    var transactionsJson = data["Transactions"].ToString();
                    _transactions = JsonSerializer.Deserialize<List<Transaction>>(transactionsJson) ?? new List<Transaction>();
                }
                
                Console.WriteLine($"Restored from backup: {_transactions.Count} transactions");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Backup load error: {ex.Message}");
            _transactions = new List<Transaction>();
        }
    }
}

public class CLI
{
    private readonly FinanceManager _manager;
    
    public CLI(FinanceManager manager)
    {
        _manager = manager;
    }
    
    public void InteractiveMode()
    {
        ShowHelp();
        
        while (true)
        {
            Console.Write("\n> ");
            var input = Console.ReadLine()?.Trim();
            
            if (string.IsNullOrEmpty(input)) continue;
            
            if (input.ToLower() == "exit" || input.ToLower() == "quit")
            {
                Console.WriteLine("Goodbye!");
                break;
            }
            
            if (input.ToLower() == "menu")
            {
                ShowMenu();
                continue;
            }
            
            if (input.ToLower() == "help")
            {
                ShowHelp();
                continue;
            }
            
            var args = ParseCommandLine(input);
            ExecuteCommand(args);
        }
    }
    
    private void ShowMenu()
    {
        Console.WriteLine("\n" + "".PadRight(40, '='));
        Console.WriteLine("MAIN MENU");
        Console.WriteLine("".PadRight(40, '-'));
        Console.WriteLine("1. Add transaction");
        Console.WriteLine("2. Monthly report");
        Console.WriteLine("3. Yearly report");  
        Console.WriteLine("4. All-time stats");
        Console.WriteLine("5. Expense forecast");
        Console.WriteLine("6. Set budget");
        Console.WriteLine("7. Import CSV");
        Console.WriteLine("8. Export CSV");
        Console.WriteLine("9. List transactions");
        Console.WriteLine("10. Search transactions");
        Console.WriteLine("11. Spending analysis");
        Console.WriteLine("12. Category rules");
        Console.WriteLine("13. Exit");
        Console.WriteLine("".PadRight(40, '-'));
        Console.Write("Choose [1-13]: ");
        
        var choice = Console.ReadLine();
        HandleMenuChoice(choice);
    }
    
    private void HandleMenuChoice(string choice)
    {
        switch (choice)
        {
            case "1":
                AddTransactionInteractive();
                break;
            case "2":
                _manager.GenerateReport("month");
                break;
            case "3":
                _manager.GenerateReport("year");
                break;
            case "4":
                _manager.GenerateReport("all");
                break;
            case "5":
                _manager.ForecastExpenses(1);
                break;
            case "6":
                SetBudgetInteractive();
                break;
            case "7":
                ImportCsvInteractive();
                break;
            case "8":
                ExportCsvInteractive();
                break;
            case "9":
                ListTransactionsInteractive();
                break;
            case "10":
                SearchTransactionsInteractive();
                break;
            case "11":
                _manager.AnalyzeSpendingPatterns();
                break;
            case "12":
                CategoryRulesInteractive();
                break;
            case "13":
                Console.WriteLine("Goodbye!");
                Environment.Exit(0);
                break;
            default:
                Console.WriteLine("Invalid choice");
                break;
        }
    }
    
    private void AddTransactionInteractive()
    {
        try
        {
            Console.WriteLine("\nADD TRANSACTION");
            
            Console.Write("Date [today]: ");
            var dateInput = Console.ReadLine();
            var date = string.IsNullOrEmpty(dateInput) ? DateTime.Now : DateTime.Parse(dateInput);
            
            Console.Write("Amount (+income, -expense): ");
            var amount = decimal.Parse(Console.ReadLine());
            
            Console.Write("Description: ");
            var note = Console.ReadLine();
            
            Console.Write("Category [auto]: ");
            var category = Console.ReadLine();
            
            var transaction = new Transaction
            {
                Date = date,
                Amount = amount,
                Note = note ?? "",
                Category = string.IsNullOrEmpty(category) ? "Other" : category
            };
            
            _manager.AddTransaction(transaction);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
    
    private void SetBudgetInteractive()
    {
        try
        {
            Console.Write("Enter monthly budget: ");
            var budget = decimal.Parse(Console.ReadLine());
            _manager.SetBudget(budget);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
    
    private void ImportCsvInteractive()
    {
        Console.Write("CSV file path: ");
        var path = Console.ReadLine();
        
        if (string.IsNullOrEmpty(path))
        {
            Console.WriteLine("Path cannot be empty");
            return;
        }
        
        _manager.ImportFromCsv(path);
    }
    
    private void ExportCsvInteractive()
    {
        Console.Write("Export file path: ");
        var path = Console.ReadLine();
        
        if (string.IsNullOrEmpty(path))
        {
            Console.WriteLine("Path cannot be empty");
            return;
        }
        
        _manager.ExportToCsv(path);
    }
    
    private void ListTransactionsInteractive()
    {
        // TODO: implement paginated transaction list
        Console.WriteLine("Transaction list feature coming soon");
    }
    
    private void SearchTransactionsInteractive()
    {
        Console.Write("Search term: ");
        var term = Console.ReadLine();
        
        if (string.IsNullOrEmpty(term))
        {
            Console.WriteLine("Search term cannot be empty");
            return;
        }
        
        var results = _manager.SearchTransactions(term);
        
        if (!results.Any())
        {
            Console.WriteLine("No transactions found");
            return;
        }
        
        Console.WriteLine($"Found {results.Count} transactions:");
        foreach (var transaction in results.Take(10)) // limit to 10 results
        {
            Console.WriteLine($"  {transaction.Date:yyyy-MM-dd} {transaction.FormattedAmount} {transaction.Category} - {transaction.Note}");
        }
    }
    
    private void CategoryRulesInteractive()
    {
        Console.WriteLine("\nCATEGORY RULES");
        Console.WriteLine("1. List rules");
        Console.WriteLine("2. Add rule");
        Console.Write("Choose [1-2]: ");
        
        var choice = Console.ReadLine();
        
        switch (choice)
        {
            case "1":
                _manager.ListCategoryRules();
                break;
            case "2":
                AddCategoryRuleInteractive();
                break;
            default:
                Console.WriteLine("Invalid choice");
                break;
        }
    }
    
    private void AddCategoryRuleInteractive()
    {
        Console.Write("Pattern (regex): ");
        var pattern = Console.ReadLine();
        
        Console.Write("Category: ");
        var category = Console.ReadLine();
        
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(category))
        {
            Console.WriteLine("Pattern and category cannot be empty");
            return;
        }
        
        _manager.AddCategoryRule(pattern, category);
    }
    
    public void ShowHelp()
    {
        Console.WriteLine("\nAVAILABLE COMMANDS:");
        Console.WriteLine("  add -d <date> -a <amount> -c <category> -n <note>  Add transaction");
        Console.WriteLine("  report <month|year|all>                            Show report");
        Console.WriteLine("  forecast [months]                                  Expense forecast");
        Console.WriteLine("  budget set <amount>                                Set budget");
        Console.WriteLine("  import <file.csv>                                  Import from CSV");
        Console.WriteLine("  export <file.csv>                                  Export to CSV");
        Console.WriteLine("  search <term>                                      Search transactions");
        Console.WriteLine("  analyze                                            Spending analysis");
        Console.WriteLine("  categories                                         Category rules");
        Console.WriteLine("  menu                                               Show menu");
        Console.WriteLine("  help                                               This help");
        Console.WriteLine("  exit                                               Exit");
        Console.WriteLine("\nEXAMPLES:");
        Console.WriteLine("  add -d \"2025-11-13\" -a -15000 -c \"Food\" -n \"Lunch\"");
        Console.WriteLine("  report month");
        Console.WriteLine("  forecast 3");
    }
    
    private string[] ParseCommandLine(string input)
    {
        var args = new List<string>();
        var currentArg = new StringBuilder();
        bool inQuotes = false;
        
        foreach (char c in input)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (currentArg.Length > 0)
                {
                    args.Add(currentArg.ToString());
                    currentArg.Clear();
                }
            }
            else
            {
                currentArg.Append(c);
            }
        }
        
        if (currentArg.Length > 0)
        {
            args.Add(currentArg.ToString());
        }
        
        return args.ToArray();
    }
    
    public void ExecuteCommand(string[] args)
    {
        if (args.Length == 0) return;
        
        var command = args[0].ToLower();
        
        try
        {
            switch (command)
            {
                case "add":
                    HandleAddCommand(args);
                    break;
                case "report":
                    HandleReportCommand(args);
                    break;
                case "forecast":
                    HandleForecastCommand(args);
                    break;
                case "budget":
                    HandleBudgetCommand(args);
                    break;
                case "import":
                    HandleImportCommand(args);
                    break;
                case "export":
                    HandleExportCommand(args);
                    break;
                case "search":
                    HandleSearchCommand(args);
                    break;
                case "analyze":
                    _manager.AnalyzeSpendingPatterns();
                    break;
                case "categories":
                    HandleCategoriesCommand(args);
                    break;
                case "help":
                    ShowHelp();
                    break;
                case "menu":
                    ShowMenu();
                    break;
                default:
                    Console.WriteLine($"Unknown command: {command}");
                    Console.WriteLine("Type 'help' for command list");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Command error: {ex.Message}");
        }
    }
    
    private void HandleAddCommand(string[] args)
    {
        DateTime? date = null;
        decimal? amount = null;
        string category = null;
        string note = null;
        
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "-d":
                case "--date":
                    if (i + 1 < args.Length) date = DateTime.Parse(args[++i]);
                    break;
                case "-a":
                case "--amount":
                    if (i + 1 < args.Length) amount = decimal.Parse(args[++i]);
                    break;
                case "-c":
                case "--category":
                    if (i + 1 < args.Length) category = args[++i];
                    break;
                case "-n":
                case "--note":
                    if (i + 1 < args.Length) note = args[++i];
                    break;
            }
        }
        
        if (!amount.HasValue)
        {
            Console.WriteLine("Amount is required");
            return;
        }
        
        var transaction = new Transaction
        {
            Date = date ?? DateTime.Now,
            Amount = amount.Value,
            Category = category ?? "Other",
            Note = note ?? ""
        };
        
        _manager.AddTransaction(transaction);
    }
    
    private void HandleReportCommand(string[] args)
    {
        var period = args.Length > 1 ? args[1] : "month";
        _manager.GenerateReport(period);
    }
    
    private void HandleForecastCommand(string[] args)
    {
        var months = args.Length > 1 ? int.Parse(args[1]) : 1;
        _manager.ForecastExpenses(months);
    }
    
    private void HandleBudgetCommand(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: budget set <amount>");
            return;
        }
        
        if (args[1].ToLower() == "set")
        {
            var amount = decimal.Parse(args[2]);
            _manager.SetBudget(amount);
        }
    }
    
    private void HandleImportCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: import <file.csv>");
            return;
        }
        
        _manager.ImportFromCsv(args[1]);
    }
    
    private void HandleExportCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: export <file.csv>");
            return;
        }
        
        _manager.ExportToCsv(args[1]);
    }
    
    private void HandleSearchCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: search <term>");
            return;
        }
        
        var results = _manager.SearchTransactions(args[1]);
        
        if (!results.Any())
        {
            Console.WriteLine("No transactions found");
            return;
        }
        
        Console.WriteLine($"Found {results.Count} transactions:");
        foreach (var transaction in results.Take(10))
        {
            Console.WriteLine($"  {transaction.Date:yyyy-MM-dd} {transaction.FormattedAmount} {transaction.Category} - {transaction.Note}");
        }
    }
    
    private void HandleCategoriesCommand(string[] args)
    {
        if (args.Length > 1 && args[1].ToLower() == "list")
        {
            _manager.ListCategoryRules();
        }
        else if (args.Length > 2 && args[1].ToLower() == "add")
        {
            _manager.AddCategoryRule(args[2], args[3]);
        }
        else
        {
            Console.WriteLine("Usage: categories list | categories add <pattern> <category>");
        }
    }
}
