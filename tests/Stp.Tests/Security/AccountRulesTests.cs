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
    public void Password_WithVietnameseDiacritics_IsRejected()
    {
        // Bộ gõ Telex/VNI biến chữ thường thành có dấu ngay khi gõ — chặn luôn
        // lúc tạo để mật khẩu sau này luôn đăng nhập được bằng bàn phím tiếng Anh.
        Assert.NotEmpty(AccountRules.ValidatePassword("mậtkhẩu"));
    }

    [Fact]
    public void Password_WithDecomposedDiacritics_IsRejected()
    {
        // Dạng ghép (e + dấu sắc = 2 ký tự) nhìn không khác dạng dựng sẵn nhưng
        // là chuỗi byte khác — cũng phải bị chặn, không trôi qua validate.
        Assert.NotEmpty(AccountRules.ValidatePassword("ca\u0301"));
    }

    [Fact]
    public void NormalizeInput_ComposesDiacritics_ToNfc()
    {
        // "ca" + dấu sắc ghép (2 ký tự) phải chuẩn hoá thành "cá" dựng sẵn —
        // cùng chuỗi byte với lúc người dùng gõ ở máy khác.
        Assert.Equal("cá", AccountRules.NormalizeInput("ca\u0301"));
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
