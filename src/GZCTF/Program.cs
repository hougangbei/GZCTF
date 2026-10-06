/*
 * GZ::CTF
 *
 * Copyright © 2022-present GZTimeWalker
 *
 * This source code is licensed under the AGPLv3 license found in the LICENSE file
 * in the root directory of this source tree.
 *
 * Identifiers related to "GZCTF" (including variations and derivations) are protected.
 * Examples include "GZCTF", "GZ::CTF", "GZCTF_FLAG", and similar constructs.
 *
 * Modifications to these identifiers are prohibited as per the LICENSE_ADDENDUM.txt
 */

global using GZCTF.Models.Data;
global using GZCTF.Utils;
global using static GZCTF.Server;
global using AppDbContext = GZCTF.Models.AppDbContext;
global using TaskStatus = GZCTF.Utils.TaskStatus;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using GZCTF.Features.Auditing.Domain;
using GZCTF.Extensions.Startup;
using GZCTF.Models;
using Serilog;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Log.Logger = LogHelper.GetInitLogger();

Banner();

var builder = WebApplication.CreateBuilder(args);

await PathHelper.EnsureDirsAsync(builder.Environment);

builder.ConfigureWebHost();
builder.ConfigureDatabase();
builder.ConfigureStorage();
builder.ConfigureCacheAndSignalR();
builder.ConfigureIdentity();
builder.ConfigureTelemetry();

builder.AddServiceConfigurations();
builder.AddCustomServices();
builder.AddWebServices();
builder.AddDevelopmentServices();

var app = builder.Build();

if (args.Length >= 2 && args[0] == "about" && args[1] == "unlock")
{
    static string? Option(string[] values, string key)
    {
        var index = Array.IndexOf(values, key);
        return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
    }

    var operatorName = Option(args, "--operator");
    var reason = Option(args, "--reason");
    if (string.IsNullOrWhiteSpace(operatorName) || string.IsNullOrWhiteSpace(reason))
    {
        Console.Error.WriteLine("Usage: GZCTF about unlock --operator <identifier> --reason <reason>");
        return;
    }

    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var state = await db.AboutPageStates.SingleOrDefaultAsync(x => x.Id == 1);
        Console.WriteLine(state?.LockOwnerId is { } ownerId
            ? $"Current editor lock: {state.LockOwnerName} ({ownerId}), created {state.LockCreatedAtUtc:O}"
            : "Current editor lock: none");
        var lockedId = state?.LockOwnerId;
        var lockedName = state?.LockOwnerName;
        if (state is not null)
        {
            state.LockOwnerId = null;
            state.LockOwnerName = null;
            state.LockCreatedAtUtc = null;
        }
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            ActorId = null,
            ActorName = operatorName.Length <= 80 ? operatorName : operatorName[..80],
            ActorKind = "operator",
            Category = "content",
            Action = "about.lock.emergency_unlock",
            TargetType = "about_page",
            TargetId = lockedId?.ToString(),
            TargetName = lockedName,
            Succeeded = true,
            HttpStatus = StatusCodes.Status200OK,
            ErrorReason = reason.Length <= 240 ? reason : reason[..240],
            RequestId = $"cli-{Guid.NewGuid():N}"[..64]
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Console.WriteLine("Emergency unlock recorded; draft content was preserved.");
    }

    await app.DisposeAsync();
    await Log.CloseAndFlushAsync();
    return;
}

Log.Logger = app.GetLogger();

await app.RunPrelaunchWorkAsync();

app.UseMiddlewares();

await app.RunServerAsync();

namespace GZCTF
{
    public class Program
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DesignTimeAppDbContextFactory))]
        static Program()
        {
            using var stream = typeof(Program).Assembly
                .GetManifestResourceStream("GZCTF.Resources.favicon.webp")!;
            DefaultFavicon = new byte[stream.Length];

            stream.ReadExactly(DefaultFavicon);
            DefaultFaviconHash = Convert.ToHexStringLower(SHA256.HashData(DefaultFavicon));
        }

        internal static byte[] DefaultFavicon { get; }
        internal static string DefaultFaviconHash { get; }
    }
}
