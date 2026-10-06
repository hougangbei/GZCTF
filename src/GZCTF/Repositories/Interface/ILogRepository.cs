using GZCTF.Models.Request.Admin;
using GZCTF.Features.Auditing.Application;

namespace GZCTF.Repositories.Interface;

public interface ILogRepository : IRepository
{
    /// <summary>
    /// Get logs with pagination and optional level filtering
    /// </summary>
    /// <param name="page"></param>
    /// <param name="pageSize"></param>
    /// <param name="level"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<PagedResult<LogMessageModel>> GetLogs(int page, int pageSize, string? level, CancellationToken token);
}
