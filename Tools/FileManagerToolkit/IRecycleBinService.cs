namespace FileManagerToolkit;

public sealed record RecycleBinItem(string Name, string Type, long SizeBytes);

public interface IRecycleBinService
{
	IReadOnlyList<RecycleBinItem> ListItems();
	void Empty();
}
