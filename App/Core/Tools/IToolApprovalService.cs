namespace Assistent.Core.Tools;

public interface IToolApprovalService
{
    Task<bool> ApproveAsync(ToolExecutionIntent intent, CancellationToken cancellationToken = default);
}
