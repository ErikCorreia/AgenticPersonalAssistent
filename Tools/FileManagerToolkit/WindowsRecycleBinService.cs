using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FileManagerToolkit;

public sealed class WindowsRecycleBinService : IRecycleBinService
{
	public IReadOnlyList<RecycleBinItem> ListItems()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("A consulta à Lixeira está disponível somente no Windows.");
		}

		return ListWindowsItems();
	}

	public void Empty()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("A Lixeira está disponível somente no Windows.");
		}

		try
		{
			var result = EmptyWindowsRecycleBin(IntPtr.Zero, null, 0x00000001 | 0x00000002);
			Marshal.ThrowExceptionForHR(result);
		}
		catch (COMException exception)
		{
			throw new IOException("O Windows não permitiu esvaziar a Lixeira.", exception);
		}
	}

	[SupportedOSPlatform("windows")]
	private static IReadOnlyList<RecycleBinItem> ListWindowsItems()
	{
		object? shell = null;
		object? folder = null;
		object? items = null;
		try
		{
			var shellType = Type.GetTypeFromProgID("Shell.Application")
				?? throw new InvalidOperationException("O Windows Shell não está disponível.");
			shell = Activator.CreateInstance(shellType)
				?? throw new InvalidOperationException("Não foi possível iniciar o Windows Shell.");
			dynamic shellApplication = shell;
			folder = shellApplication.NameSpace(10);
			if (folder is null)
			{
				return Array.Empty<RecycleBinItem>();
			}

			dynamic recycleBin = folder;
			items = recycleBin.Items();
			dynamic recycleBinItems = items;
			var results = new List<RecycleBinItem>();
			for (var index = 0; index < (int)recycleBinItems.Count; index++)
			{
				object? item = null;
				try
				{
					item = recycleBinItems.Item(index);
					dynamic shellItem = item;
					results.Add(new RecycleBinItem(
						Convert.ToString(shellItem.Name) ?? string.Empty,
						Convert.ToString(shellItem.Type) ?? string.Empty,
						Convert.ToInt64(shellItem.Size)));
				}
				finally
				{
					ReleaseComObject(item);
				}
			}

			return results;
		}
		catch (Exception exception) when (exception is COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
		{
			throw new IOException("O Windows não permitiu consultar os itens da Lixeira.", exception);
		}
		finally
		{
			ReleaseComObject(items);
			ReleaseComObject(folder);
			ReleaseComObject(shell);
		}
	}

	[SupportedOSPlatform("windows")]
	private static void ReleaseComObject(object? value)
	{
		if (value is not null && Marshal.IsComObject(value))
		{
			Marshal.ReleaseComObject(value);
		}
	}

	[DllImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern int EmptyWindowsRecycleBin(IntPtr windowHandle, string? rootPath, uint flags);
}
