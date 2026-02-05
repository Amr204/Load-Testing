using System.Text.Json;
using DFrame.Controller;

namespace CashlessLoadTest.Controller;

/// <summary>
/// Flat file log execution result history provider.
/// Saves execution results to JSON files in a specified directory.
/// </summary>
public class FlatFileLogExecutionResultHistoryProvider : IExecutionResultHistoryProvider
{
    private readonly string _rootDir;
    private readonly IExecutionResultHistoryProvider _memoryProvider;

    public event Action? NotifyCountChanged;

    public FlatFileLogExecutionResultHistoryProvider(string rootDir)
    {
        _rootDir = rootDir;
        _memoryProvider = new InMemoryExecutionResultHistoryProvider();
        
        // Ensure directory exists
        Directory.CreateDirectory(_rootDir);
    }

    public int GetCount()
    {
        return _memoryProvider.GetCount();
    }

    public IReadOnlyList<ExecutionSummary> GetList()
    {
        return _memoryProvider.GetList();
    }

    public (ExecutionSummary Summary, SummarizedExecutionResult[] Results)? GetResult(ExecutionId executionId)
    {
        return _memoryProvider.GetResult(executionId);
    }

    public void AddNewResult(ExecutionSummary summary, SummarizedExecutionResult[] results)
    {
        // Generate filename with timestamp, workload name, and execution ID
        var fileName = $"{summary.StartTime:yyyy-MM-dd HH.mm.ss} {summary.Workload} {summary.ExecutionId}.json";
        var filePath = Path.Combine(_rootDir, fileName);

        // Serialize to JSON with indentation for readability
        var json = JsonSerializer.Serialize(
            new { summary, results },
            new JsonSerializerOptions { WriteIndented = true }
        );

        // Write to file
        File.WriteAllText(filePath, json);
        Console.WriteLine($"[DFrame] Execution result saved to: {filePath}");

        // Also store in memory for quick access
        _memoryProvider.AddNewResult(summary, results);
        
        // Notify listeners that count changed
        NotifyCountChanged?.Invoke();
    }
}
