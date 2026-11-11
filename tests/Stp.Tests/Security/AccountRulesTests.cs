using Stp.Core.Security;

namespace Stp.Tests.Security;

/// <summary>
/// Quy tắc dữ liệu tài khoản nhóm — cùng một bộ thông báo tiếng Việt mà
/// UsersController dùng để trả lỗi về cho người dùng.
/// </summary>
public sealed class AccountRulesTests
{
    [Fact]
    public void Username_BelowMinimum_IsRejected()
    {
        Assert.NotEmpty(AccountRules.ValidateUsername("ab"));
    }

    [Fact]
    public void Username_AboveMaximum_IsRejected()
    {
        Assert.NotEmpty(AccountRules.ValidateUsername(new string('a', AccountRules.MaxUsername + 1)));
    }

    [Fact]
    public void Username_WithVietnameseToneMarks_IsRejected()
    {
        Assert.NotEmpty(AccountRules.ValidateUsername("lân"));
    }

    [Fact]
    public void Username_WithSpace_IsRejected()
    {
        Assert.NotEmpty(AccountRules.ValidateUsername("minh quang"));
    }

    [Fact]
    public void Username_NormalCase_IsAccepted()
    {
        Assert.Empty(AccountRules.ValidateUsername("minh.quang"));
        Assert.Empty(AccountRules.ValidateUsername("lan"));
        Assert.Empty(AccountRules.ValidateUsername("Kho-1990"));
    }

    [Fact]
    public void Password_TooShort_IsRejected()
    {
        Assert.NotEmpty(AccountRules.ValidatePassword("12345"));
    }

    [Fact]
    public void Password_Valid_IsAccepted()
    {
        Assert.Empty(AccountRules.ValidatePassword("stp2026"));
    }

    [Fact]
    public void DisplayName_SingleChar_IsRejected()
    {
        Assert.NotEmpty(AccountRules.ValidateDisplayName("L"));
    }

    [Fact]
    public void DisplayName_Vietnamese_IsAccepted()
    {
        Assert.Empty(AccountRules.ValidateDisplayName("Lan Nguyễn Thị"));
    }

    [Fact]
    public void Role_Invalid_IsRejected()
    {
        Assert.NotEmpty(AccountRules.ValidateRole("owner"));
    }

    [Fact]
    public void Role_Valid_IsAccepted()
    {
        Assert.Empty(AccountRules.ValidateRole(AccountRules.RoleAdmin));
        Assert.Empty(AccountRules.ValidateRole(AccountRules.RoleMember));
    }
}
