using GZCTF.Features.QqBot.Application;
using Xunit;

namespace GZCTF.Test.UnitTests.Features.QqBot;

public sealed class QqMessageTemplateTests
{
    [Fact]
    public void Renders_member_challenge_team_and_source_without_leaking_unknown_placeholders()
    {
        var result = QqMessageTemplate.Render(
            "{member} 解出了 {challenge}（{source} / {team}）",
            new QqSolveEvent("Alice", "Web 入门", "技能树", "二年级"));

        Assert.Equal("Alice 解出了 Web 入门（技能树 / 二年级）", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{unknown}")]
    [InlineData("text {member")]
    public void Rejects_empty_or_invalid_templates(string template)
    {
        Assert.False(QqMessageTemplate.IsValid(template));
    }

    [Fact]
    public void Default_template_is_valid()
    {
        Assert.True(QqMessageTemplate.IsValid(QqBotSettings.DefaultTemplate));
    }

    [Fact]
    public void Escapes_cq_markup_in_member_names()
    {
        var result = QqMessageTemplate.Render("{member} solved {challenge}",
            new QqSolveEvent("[CQ:at,qq=all]", "Web", "比赛"));
        Assert.Equal("&#91;CQ:at,qq=all&#93; solved Web", result);
    }
}
