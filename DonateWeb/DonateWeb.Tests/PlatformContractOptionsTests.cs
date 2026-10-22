using DonateWeb.Services;
using Xunit;

namespace DonateWeb.Tests;

public sealed class PlatformContractOptionsTests
{
    [Fact]
    public void CompleteRealValuesAllowContractSigning()
    {
        var options = new PlatformContractOptions
        {
            OperatorCapacity = "Cá nhân",
            OperatorLegalName = "Nguyễn Anh Hiếu",
            OperatorAddress = "Số 1, đường Lê Lợi, phường Bến Nghé, TP. Hồ Chí Minh",
            OperatorRegistrationNumber = "123456789012",
            ContactEmail = "cupitcookies@gmail.com"
        };

        Assert.True(options.IsComplete);
    }

    [Theory]
    [InlineData("Thông tin minh họa (bài tập lớn)")]
    [InlineData("Không áp dụng trong bản demo cá nhân")]
    [InlineData("Không áp dụng")]
    [InlineData("CHANGE_ME")]
    [InlineData("YOUR_OPERATOR_ADDRESS")]
    public void PlaceholderAddressBlocksContractSigning(string placeholder)
    {
        var options = CompleteOptions();
        options.OperatorAddress = placeholder;

        Assert.False(options.IsComplete);
    }

    [Theory]
    [InlineData("Không áp dụng trong bản demo cá nhân")]
    [InlineData("Không áp dụng")]
    [InlineData("CHANGE_ME")]
    public void PlaceholderRegistrationNumberBlocksContractSigning(string placeholder)
    {
        var options = CompleteOptions();
        options.OperatorRegistrationNumber = placeholder;

        Assert.False(options.IsComplete);
    }

    [Theory]
    [InlineData("contact@example.test")]
    [InlineData("not-an-email")]
    public void PlaceholderOrInvalidContactEmailBlocksContractSigning(string email)
    {
        var options = CompleteOptions();
        options.ContactEmail = email;

        Assert.False(options.IsComplete);
    }

    [Fact]
    public void IncompleteOperatorAddressBlocksContractSigning()
    {
        var options = CompleteOptions();
        options.OperatorAddress = "Hà Nội";

        Assert.False(options.IsComplete);
    }

    private static PlatformContractOptions CompleteOptions() => new()
    {
        OperatorCapacity = "Cá nhân",
        OperatorLegalName = "Nguyễn Anh Hiếu",
        OperatorAddress = "Số 1, đường Lê Lợi, phường Bến Nghé, TP. Hồ Chí Minh",
        OperatorRegistrationNumber = "123456789012",
        ContactEmail = "cupitcookies@gmail.com"
    };
}
