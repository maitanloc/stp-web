using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stp.Api.Controllers;
using Stp.Api.Hubs;
using Stp.Api.Services;
using Stp.Contracts;
using Stp.Core.Domain;
using Stp.Core.Security;
using Stp.Core.Storage;
using Stp.Infrastructure.Persistence;
using Xunit;

namespace Stp.Tests.Security;

public sealed class ChatAndProfileTests
{
    [Fact]
    public void DisplayName_Validation_WorksCorrectly()
    {
        Assert.NotEmpty(AccountRules.ValidateDisplayName(""));
        Assert.NotEmpty(AccountRules.ValidateDisplayName("   "));
        Assert.Empty(AccountRules.ValidateDisplayName("Nguyễn Văn A"));
        Assert.Empty(AccountRules.ValidateDisplayName("Cán bộ trích yếu 01"));
    }

    [Fact]
    public void Password_Validation_WorksCorrectly()
    {
        Assert.NotEmpty(AccountRules.ValidatePassword("12345"));
        Assert.Empty(AccountRules.ValidatePassword("123456"));
        Assert.Empty(AccountRules.ValidatePassword("STP@2026!secret"));
    }

    [Fact]
    public void ChatUploadResponse_Record_InitializesProperly()
    {
        var response = new ChatUploadResponse("/api/team/chat/attachment/test.png", "test.png", "image", 1024);
        Assert.Equal("/api/team/chat/attachment/test.png", response.Url);
        Assert.Equal("test.png", response.FileName);
        Assert.Equal("image", response.FileType);
        Assert.Equal(1024, response.SizeBytes);
    }

    [Fact]
    public void ProfileStatsResponse_Record_InitializesProperly()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var stats = new ProfileStatsResponse(id, "admin", "Quản trị viên", "admin", now, now, 1, 10, 5, 2);
        
        Assert.Equal(id, stats.Id);
        Assert.Equal("admin", stats.Username);
        Assert.Equal("Quản trị viên", stats.DisplayName);
        Assert.Equal(10, stats.TotalActions);
        Assert.Equal(5, stats.ChatMessagesCount);
        Assert.Equal(2, stats.SummarySessionsCount);
    }
}

