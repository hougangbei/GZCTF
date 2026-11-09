using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using GZCTF.Features.ChallengeLibrary.Domain;
using GZCTF.Features.SkillTrees.Domain;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.SkillTrees;

[Collection(nameof(IntegrationTestCollection))]
public class ImportedDescriptionRepairTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task Repairing_only_visible_English_body_reports_hidden_Chinese_and_filling_it_allows_publication()
    {
        Guid categoryId, skillId, challengeId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = new SkillCategory { Name = "Imported category", IconKey = "web" };
            var skill = new Skill { Name = "Imported skill", IconKey = "web", IsEnabled = true };
            var challenge = new Challenge {
                Type = ChallengeType.StaticAttachment, IsEnabled = true, Score = 100,
                PublicationState = ChallengePublicationState.Draft,
                SourceType = "CTFD", SourceId = Guid.NewGuid().ToString(),
                Localizations = [new() { Locale = "zh", Title = "Imported title", Body = "" }, new() { Locale = "en", Title = "Imported title", Body = "" }],
                Flags = [new() { Kind = ChallengeFlagKind.Static, Value = "flag{synthetic_description}" }]
            };
            db.SkillCategories.Add(category); db.Skills.Add(skill); db.Challenges.Add(challenge);
            await db.SaveChangesAsync(); categoryId = category.Id; skillId = skill.Id; challengeId = challenge.Id;
        }
        const string password = "S07!AdminPassword";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), password, role: Role.Admin);
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        var created = await admin.PostAsJsonAsync("/api/admin/skill-trees", new { name = "Repair imported descriptions", summary = "Repair QA", iconKey = "web" });
        created.EnsureSuccessStatusCode();
        var treeId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["skillTreeId"]!.GetValue<Guid>();
        var path = $"/api/admin/skill-trees/{treeId}/workspace";
        var workspace = (await admin.GetFromJsonAsync<JsonObject>(path))!;
        var document = new { name = "Repair imported descriptions", summary = "Repair QA", iconKey = "web", categories = new[] {
            new { categoryId, name = "Imported category", summary = "", iconKey = "web", skills = new[] {
                new { skillId, name = "Imported skill", summary = "", iconKey = "web", isEnabled = true, contents = new[] {
                    new { kind = "challenge", contentId = challengeId, title = "Imported title", summary = "", state = "Draft" }
                } }
            } }
        } };
        (await admin.PutAsJsonAsync(path, new { version = workspace["version"]!.GetValue<string>(), document })).EnsureSuccessStatusCode();
        var editPath = $"/api/admin/challenges/{challengeId}/edit?course={treeId}";
        var edit = (await admin.GetFromJsonAsync<JsonObject>(editPath))!;
        async Task SaveBodies(uint rowVersion, string zh) => (await admin.PutAsJsonAsync($"/api/admin/challenges/{challengeId}?course={treeId}", new {
            rowVersion, localizations = new[] { new { locale = "zh", title = "Imported title", body = zh }, new { locale = "en", title = "Imported title", body = "Repaired description" } }
        })).EnsureSuccessStatusCode();
        await SaveBodies(edit["publication"]!["rowVersion"]!.GetValue<uint>(), "");
        workspace = (await admin.GetFromJsonAsync<JsonObject>(path))!;
        var version = workspace["version"]!.GetValue<string>();
        var check = (await (await admin.PostAsJsonAsync(path + "/check", new { version })).Content.ReadFromJsonAsync<JsonObject>())!;
        var issue = Assert.Single(check["issues"]!.AsArray(), i => i!["code"]!.GetValue<string>() == "challenge_body_missing");
        Assert.Equal(new[] { "zh" }, issue!["locales"]!.AsArray().Select(l => l!.GetValue<string>()).ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path + "/publish", new { version })).StatusCode);
        edit = (await admin.GetFromJsonAsync<JsonObject>(editPath))!;
        await SaveBodies(edit["publication"]!["rowVersion"]!.GetValue<uint>(), "Repaired description");
        workspace = (await admin.GetFromJsonAsync<JsonObject>(path))!; version = workspace["version"]!.GetValue<string>();
        check = (await (await admin.PostAsJsonAsync(path + "/check", new { version })).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.DoesNotContain(check["issues"]!.AsArray(), i => i!["severity"]!.GetValue<string>() == "error");
        (await admin.PostAsJsonAsync(path + "/publish", new { version })).EnsureSuccessStatusCode();
        await using var verify = factory.Services.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var original = await context.Challenges.Include(c => c.Localizations).SingleAsync(c => c.Id == challengeId);
        Assert.All(original.Localizations, text => Assert.Equal("", text.Body));
        Assert.Equal(100, original.Score);
    }
}
