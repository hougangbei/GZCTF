using GZCTF.Models.Request.Admin;
using GZCTF.Features.Auditing.Application;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Repositories;

public class LogRepository(AppDbContext context) : RepositoryBase(context), ILogRepository
{
    public async Task<PagedResult<LogMessageModel>> GetLogs(int page, int pageSize, string? level, CancellationToken token)
    {
        IQueryable<LogModel> data = Context.Logs;

        if (level is not null && level != "All")
            data = data.Where(x => x.Level == level);
        var total = await data.CountAsync(token);
        var items = await data.OrderByDescending(x => x.TimeUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(log => LogMessageModel.FromLogModel(log)).ToArrayAsync(token);
        return new PagedResult<LogMessageModel>(items, total, page, pageSize);
    }
}
