using FluentAssertions;

namespace Bot.Tests;

[TestClass]
public class StringExtensionsTests
{
    [DataTestMethod]
    [DataRow("/help", "/help")]
    [DataRow(" /help", "/help")]
    [DataRow("/help asidhais", "/help")]
    [DataRow("/help@botname", "/help")]
    [DataRow(" /help@botname", "/help")]
    [DataRow("/help@botname ", "/help")]
    [DataRow("/help@botname asdasd", "/help")]
    public void Command_parsed_correctly(string text, string expectedCommand)
    {    
        // Arrange & Act
        var command = text.GetCommand();
    
        // Assert
        command.ToString().Should().Be(expectedCommand);
    }
}