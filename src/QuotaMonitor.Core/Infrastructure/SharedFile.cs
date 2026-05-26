namespace QuotaMonitor.Core.Infrastructure;

public static class SharedFile
{
    public static List<string> ReadAllLines(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        var lines = new List<string>();
        while (!reader.EndOfStream)
        {
            lines.Add(reader.ReadLine());
        }

        return lines;
    }
}
