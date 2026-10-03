using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Assistent.Core.Tools;

namespace FileManagerToolkit;

public sealed class FileManager : IAssistantTool
{
	private const int MaxFileSizeBytes = 1_000_000;
	private const int MaxRecycleBinItems = 1000;
	private readonly string[] _writableRoots;
	private readonly string[] _readOnlyRoots;
	private readonly IRecycleBinService _recycleBin;

	public FileManager() : this(GetConfiguredRoots(), GetConfiguredReadOnlyRoots(), new WindowsRecycleBinService())
	{
	}

	public FileManager(IEnumerable<string> allowedRoots) : this(
		allowedRoots,
		Array.Empty<string>(),
		new WindowsRecycleBinService())
	{
	}

	public FileManager(
		IEnumerable<string> writableRoots,
		IEnumerable<string> readOnlyRoots,
		IRecycleBinService recycleBin)
	{
		ArgumentNullException.ThrowIfNull(recycleBin);
		_writableRoots = writableRoots
			.Select(Path.GetFullPath)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (_writableRoots.Length == 0)
		{
			throw new ArgumentException("Pelo menos uma pasta gravável deve ser configurada.", nameof(writableRoots));
		}
		_readOnlyRoots = readOnlyRoots
			.Select(Path.GetFullPath)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		_recycleBin = recycleBin;
	}

	public string Name => "file_manager";

	public string Description => "Ações: list/search/read consultam apenas pastas graváveis ou permitidas para leitura e pedem consentimento antes de enviar dados locais ao provedor configurado; system_info consulta uso de espaço em disco e memória RAM com consentimento; write/create_directory/delete exigem confirmação e só atuam em pastas graváveis; list_recycle_bin consulta metadados da Lixeira com consentimento; empty_recycle_bin exige a frase de confirmação específica.";

	public object ParameterSchema => new
	{
		type = "OBJECT",
		properties = new
		{
			action = new { type = "STRING", @enum = new[] { "list", "search", "read", "write", "create_directory", "delete", "list_recycle_bin", "empty_recycle_bin", "system_info" } },
			path = new { type = "STRING" },
			pattern = new { type = "STRING" },
			recursive = new { type = "BOOLEAN" },
			maxResults = new { type = "INTEGER" },
			content = new { type = "STRING" }
		},
		required = new[] { "action" }
	};

	public ToolExecutionIntent GetExecutionIntent(IDictionary<string, object?> arguments)
	{
		var action = GetString(arguments, "action").ToLowerInvariant();
		if (action == "list_recycle_bin")
		{
			return new(false, true, "Consultar nomes e metadados dos itens da Lixeira e enviar o resultado ao provedor configurado.");
		}
		if (action == "empty_recycle_bin")
		{
			return new(
				true,
				false,
				"Esvaziar permanentemente a Lixeira de todos os volumes do Windows.",
				"ESVAZIAR LIXEIRA");
		}
		if (action == "system_info")
		{
			return new(false, true, "Consultar o uso de espaço em disco e de memória RAM e enviar esses dados ao provedor configurado.");
		}

		var path = GetString(arguments, "path", action is "list" or "search" ? _writableRoots[0] : null);
		return action switch
		{
			"read" => new(false, true, $"Ler o arquivo '{path}' e enviar seu conteúdo ao provedor configurado."),
			"list" or "search" => new(false, true, $"Consultar nomes e metadados em '{path}' e enviar o resultado ao provedor configurado."),
			"write" => new(true, false, $"Escrever no arquivo '{path}'."),
			"delete" => new(true, false, $"Excluir o caminho '{path}'."),
			"create_directory" => new(true, false, $"Criar a pasta '{path}'."),
			_ => ToolExecutionIntent.Safe
		};
	}

	public async Task<object?> InvokeAsync(
		IDictionary<string, object?> arguments,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var action = GetString(arguments, "action");
			object result = action.ToLowerInvariant() switch
			{
				"list" => List(GetString(arguments, "path", _writableRoots[0])),
				"search" => Search(
					GetString(arguments, "path", _writableRoots[0]),
					GetString(arguments, "pattern", "*"),
					GetBoolean(arguments, "recursive", true),
					GetInteger(arguments, "maxResults", 100)),
				"create_directory" => CreateDirectory(GetString(arguments, "path")),
				"delete" => Delete(GetString(arguments, "path")),
				"list_recycle_bin" => ListRecycleBin(),
				"empty_recycle_bin" => EmptyRecycleBin(),
				"system_info" => GetSystemInfo(),
				_ => Error("Ação inválida. Use uma ação de arquivos, system_info ou uma ação da Lixeira.")
			};

			if (action.Equals("read", StringComparison.OrdinalIgnoreCase))
			{
				result = await ReadAsync(GetString(arguments, "path"), cancellationToken);
			}
			else if (action.Equals("write", StringComparison.OrdinalIgnoreCase))
			{
				result = await WriteAsync(
					GetString(arguments, "path"),
					GetString(arguments, "content"),
					cancellationToken);
			}

			return result;
		}
		catch (UnauthorizedAccessException)
		{
			return Error("Acesso negado para o caminho informado ou operação protegida para preservar o Windows.");
		}
		catch (IOException exception)
		{
			return Error($"Erro de entrada/saída: {exception.Message}");
		}
		catch (ArgumentException exception)
		{
			return Error(exception.Message);
		}
		catch (PlatformNotSupportedException exception)
		{
			return Error(exception.Message);
		}
	}

	private object List(string pathValue)
	{
		var directory = ResolvePath(pathValue, PathAccess.List);
		if (!Directory.Exists(directory))
		{
			return Error("A pasta informada não existe.");
		}

		var entries = Directory.EnumerateFileSystemEntries(directory)
			.Select(path => new
			{
				Name = Path.GetFileName(path),
				Type = Directory.Exists(path) ? "directory" : "file",
				Path = path
			})
			.OrderBy(entry => entry.Type)
			.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
			.Take(1000)
			.ToArray();

		return new { Success = true, Action = "list", Entries = entries };
	}

	private object Search(string pathValue, string pattern, bool recursive, int maxResults)
	{
		var directory = ResolvePath(pathValue, PathAccess.Search);
		if (!Directory.Exists(directory))
		{
			return Error("A pasta informada não existe.");
		}
		if (Path.GetFileName(pattern) != pattern || string.IsNullOrWhiteSpace(pattern))
		{
			return Error("O padrão de busca deve ser um nome de arquivo, sem segmentos de caminho.");
		}

		maxResults = Math.Clamp(maxResults, 1, 1000);
		var options = new EnumerationOptions
		{
			RecurseSubdirectories = recursive,
			AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
			IgnoreInaccessible = false,
			MaxRecursionDepth = 8
		};
		var matches = new List<object>();

		try
		{
			foreach (var path in Directory.EnumerateFileSystemEntries(directory, pattern, options))
			{
				matches.Add(new
				{
					Name = Path.GetFileName(path),
					Type = Directory.Exists(path) ? "directory" : "file",
					Path = path
				});

				if (matches.Count >= maxResults)
				{
					break;
				}
			}
		}
		catch (UnauthorizedAccessException)
		{
			return Error("A busca encontrou uma pasta sem permissão de leitura.");
		}

		return new { Success = true, Action = "search", Pattern = pattern, Results = matches };
	}

	private async Task<object> ReadAsync(string pathValue, CancellationToken cancellationToken)
	{
		var path = ResolvePath(pathValue, PathAccess.Read);
		if (!File.Exists(path))
		{
			return Error("O arquivo informado não existe.");
		}
		if (new FileInfo(path).Length > MaxFileSizeBytes)
		{
			return Error($"O arquivo excede o limite de leitura de {MaxFileSizeBytes} bytes.");
		}

		var content = await File.ReadAllTextAsync(path, cancellationToken);
		return new
		{
			Success = true,
			Action = "read",
			Path = path,
			Content = content
		};
	}

	private async Task<object> WriteAsync(
		string pathValue,
		string content,
		CancellationToken cancellationToken)
	{
		if (System.Text.Encoding.UTF8.GetByteCount(content) > MaxFileSizeBytes)
		{
			return Error($"O conteúdo excede o limite de escrita de {MaxFileSizeBytes} bytes.");
		}

		var path = ResolvePath(pathValue, PathAccess.Write);
		var directory = Path.GetDirectoryName(path);
		if (directory is not null)
		{
			Directory.CreateDirectory(directory);
		}

		await File.WriteAllTextAsync(path, content, cancellationToken);
		return new
		{
			Success = true,
			Action = "write",
			Path = path
		};
	}

	private object CreateDirectory(string pathValue)
	{
		var path = ResolvePath(pathValue, PathAccess.Write);
		Directory.CreateDirectory(path);
		return new
		{
			Success = true,
			Action = "create_directory",
			Path = path
		};
	}

	private object Delete(string pathValue)
	{
		var path = ResolvePath(pathValue, PathAccess.Write);
		if (_writableRoots.Any(root => path.Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)))
		{
			return Error("A exclusão de uma pasta autorizada inteira foi bloqueada.");
		}
		if (IsProtectedPath(path))
		{
			return Error("A remoção deste caminho foi bloqueada para preservar a integridade do Windows.");
		}

		if (File.Exists(path))
		{
			File.Delete(path);
		}
		else if (Directory.Exists(path))
		{
			Directory.Delete(path, recursive: true);
		}
		else
		{
			return Error("O arquivo ou diretório informado não existe.");
		}

		return new
		{
			Success = true,
			Action = "delete",
			Path = path
		};
	}

	private string ResolvePath(string pathValue, PathAccess access)
	{
		if (string.IsNullOrWhiteSpace(pathValue))
		{
			throw new ArgumentException("O caminho é obrigatório.");
		}

		var fullPath = Path.IsPathRooted(pathValue)
			? Path.GetFullPath(pathValue)
			: Path.GetFullPath(pathValue, _writableRoots[0]);
		if (IsDriveRoot(fullPath) && access != PathAccess.List)
		{
			throw new UnauthorizedAccessException("A raiz de uma unidade só pode ser listada, não pesquisada nem modificada.");
		}
		if (access == PathAccess.Write
			&& _readOnlyRoots.Any(readOnlyRoot => IsWithinRoot(fullPath, readOnlyRoot)))
		{
			throw new UnauthorizedAccessException("Esta pasta está marcada como somente leitura.");
		}

		var root = _writableRoots.FirstOrDefault(allowedRoot => IsWithinRoot(fullPath, allowedRoot));
		if (root is null && access == PathAccess.List && IsDriveRoot(fullPath))
		{
			root = fullPath;
		}
		if (root is null && access is PathAccess.List or PathAccess.Search or PathAccess.Read)
		{
			root = _readOnlyRoots.FirstOrDefault(readOnlyRoot => IsWithinRoot(fullPath, readOnlyRoot));
		}
		if (root is null)
		{
			throw new UnauthorizedAccessException(
				access == PathAccess.Write
					? "O caminho não está dentro de uma pasta gravável autorizada."
					: "O caminho não está dentro de uma pasta autorizada para leitura.");
		}

		RejectReparsePoints(fullPath, root);
		return fullPath;
	}

	private object ListRecycleBin()
	{
		var items = _recycleBin.ListItems();
		return new
		{
			Success = true,
			Action = "list_recycle_bin",
			Items = items.Take(MaxRecycleBinItems),
			Truncated = items.Count > MaxRecycleBinItems
		};
	}

	private object EmptyRecycleBin()
	{
		_recycleBin.Empty();
		return new { Success = true, Action = "empty_recycle_bin", Message = "A Lixeira foi esvaziada." };
	}

	private static object GetSystemInfo()
	{
		var memory = GetPhysicalMemoryUsage();

		var drives = new List<object>();
		var driveErrors = new List<object>();
		foreach (var drive in DriveInfo.GetDrives())
		{
			try
			{
				if (!drive.IsReady)
				{
					driveErrors.Add(new { Name = drive.Name, Error = "Unidade não está pronta." });
					continue;
				}

				var totalBytes = drive.TotalSize;
				var availableBytes = drive.AvailableFreeSpace;
				var usedBytes = Math.Max(0, totalBytes - availableBytes);
				drives.Add(new
				{
					Name = drive.Name,
					Label = drive.VolumeLabel,
					Type = drive.DriveType.ToString(),
					Format = drive.DriveFormat,
					TotalBytes = totalBytes,
					UsedBytes = usedBytes,
					AvailableBytes = availableBytes,
					UsagePercentage = totalBytes == 0 ? 0 : Math.Round(usedBytes * 100d / totalBytes, 1)
				});
			}
			catch (UnauthorizedAccessException exception)
			{
				driveErrors.Add(new { Name = drive.Name, Error = exception.Message });
			}
			catch (IOException exception)
			{
				driveErrors.Add(new { Name = drive.Name, Error = exception.Message });
			}
			catch (InvalidOperationException exception)
			{
				driveErrors.Add(new { Name = drive.Name, Error = exception.Message });
			}
		}

		return new
		{
			Success = true,
			Action = "system_info",
			Memory = memory,
			Drives = drives,
			DriveErrors = driveErrors
		};
	}

	private static object GetPhysicalMemoryUsage()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("A consulta de memória RAM do sistema está disponível somente no Windows.");
		}

		var status = new MemoryStatusEx
		{
			Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
		};
		if (!GlobalMemoryStatusEx(ref status))
		{
			throw new IOException(
				"Não foi possível consultar o uso de memória RAM do sistema.",
				new Win32Exception(Marshal.GetLastWin32Error()));
		}

		var usedBytes = status.TotalPhysical - status.AvailablePhysical;
		return new
		{
			TotalBytes = status.TotalPhysical,
			UsedBytes = usedBytes,
			AvailableBytes = status.AvailablePhysical,
			UsagePercentage = status.TotalPhysical == 0 ? 0 : Math.Round(usedBytes * 100d / status.TotalPhysical, 1)
		};
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MemoryStatusEx
	{
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhysical;
		public ulong AvailablePhysical;
		public ulong TotalPageFile;
		public ulong AvailablePageFile;
		public ulong TotalVirtual;
		public ulong AvailableVirtual;
		public ulong AvailableExtendedVirtual;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

	private static bool IsProtectedPath(string path)
	{
		var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
		var root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar);
		if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		var protectedDirectories = new[]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.Windows),
			Environment.GetFolderPath(Environment.SpecialFolder.System),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
		}.Where(directory => !string.IsNullOrWhiteSpace(directory));

		return protectedDirectories.Any(directory =>
			fullPath.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
			|| fullPath.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
	}

	private static string GetString(IDictionary<string, object?> arguments, string name, string? defaultValue = null)
	{
		if (arguments.TryGetValue(name, out var value) && value is not null)
		{
			return value.ToString() ?? defaultValue ?? string.Empty;
		}

		if (defaultValue is not null)
		{
			return defaultValue;
		}

		throw new ArgumentException($"O argumento '{name}' é obrigatório.");
	}

	private static bool GetBoolean(IDictionary<string, object?> arguments, string name, bool defaultValue)
	{
		if (!arguments.TryGetValue(name, out var value) || value is null)
		{
			return defaultValue;
		}

		if (!bool.TryParse(value.ToString(), out var result))
		{
			throw new ArgumentException($"O argumento '{name}' deve ser verdadeiro ou falso.");
		}

		return result;
	}

	private static int GetInteger(IDictionary<string, object?> arguments, string name, int defaultValue)
	{
		if (!arguments.TryGetValue(name, out var value) || value is null)
		{
			return defaultValue;
		}

		if (!int.TryParse(value.ToString(), out var result))
		{
			throw new ArgumentException($"O argumento '{name}' deve ser um número inteiro.");
		}

		return result;
	}

	private static string[] GetConfiguredRoots()
	{
		var configuredRoots = Environment.GetEnvironmentVariable("ASSISTENT_ALLOWED_FILE_ROOTS");
		if (!string.IsNullOrWhiteSpace(configuredRoots))
		{
			return configuredRoots.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		}

		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return string.IsNullOrWhiteSpace(userProfile) ? Array.Empty<string>() : new[] { userProfile };
	}

	private static string[] GetConfiguredReadOnlyRoots()
	{
		var configuredRoots = Environment.GetEnvironmentVariable("ASSISTENT_READ_ONLY_ROOTS");
		var systemRoots = new[]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.Windows),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
		}.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
		var additionalRoots = string.IsNullOrWhiteSpace(configuredRoots)
			? Array.Empty<string>()
			: configuredRoots.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return systemRoots.Concat(additionalRoots).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static bool IsDriveRoot(string path) =>
		string.Equals(path, Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase);

	private enum PathAccess
	{
		List,
		Search,
		Read,
		Write
	}

	private static bool IsWithinRoot(string path, string root)
	{
		var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
		var rootPrefix = Path.EndsInDirectorySeparator(normalizedRoot)
			? normalizedRoot
			: normalizedRoot + Path.DirectorySeparatorChar;
		return path.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
	}

	private static void RejectReparsePoints(string path, string root)
	{
		var relativePath = Path.GetRelativePath(root, path);
		var currentPath = root;
		if (relativePath == ".")
		{
			return;
		}

		foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
		{
			currentPath = Path.Combine(currentPath, segment);
			if ((File.Exists(currentPath) || Directory.Exists(currentPath))
				&& File.GetAttributes(currentPath).HasFlag(FileAttributes.ReparsePoint))
			{
				throw new UnauthorizedAccessException("Caminhos que atravessam links ou pontos de nova análise não são permitidos.");
			}
		}
	}

	private static object Error(string message)
	{
		return new { Success = false, Error = message };
	}

}
