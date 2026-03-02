using UtilityAi.Compass.Abstractions.Facts;
using UtilityAi.Compass.StandardModules;
using UtilityAi.Utils;

namespace UtilityAi.Compass.Tests;

public class FileCreationModuleTests
{
    [Fact]
    public void Propose_ReturnsProposal_WhenUserRequestExists()
    {
        var module = new FileCreationModule();
        var bus = new EventBus();
        bus.Publish(new UserRequest("create file test.txt with content hello"));
        var rt = new UtilityAi.Utils.Runtime(bus, 0);

        var proposals = module.Propose(rt).ToList();

        Assert.Single(proposals);
        Assert.Equal("file-creation.write", proposals[0].Id);
        Assert.Equal("Create a file with the specified content", proposals[0].Description);
    }

    [Fact]
    public void Propose_ReturnsEmpty_WhenNoUserRequest()
    {
        var module = new FileCreationModule();
        var bus = new EventBus();
        var rt = new UtilityAi.Utils.Runtime(bus, 0);

        var proposals = module.Propose(rt).ToList();

        Assert.Empty(proposals);
    }

    [Fact]
    public async Task Propose_ActCreatesFile_WhenExecuted()
    {
        var module = new FileCreationModule();
        var bus = new EventBus();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.txt");

        try
        {
            bus.Publish(new UserRequest($"create file {filePath} with content hello world"));
            var rt = new UtilityAi.Utils.Runtime(bus, 0);

            var proposals = module.Propose(rt).ToList();
            Assert.Single(proposals);

            await proposals[0].Act(CancellationToken.None);

            Assert.True(File.Exists(filePath));
            Assert.Equal("hello world", File.ReadAllText(filePath));

            var response = bus.GetOrDefault<AiResponse>();
            Assert.NotNull(response);
            Assert.Contains(filePath, response.Text);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Propose_ActPublishesFailure_WhenPathIsDirectory()
    {
        var module = new FileCreationModule();
        var bus = new EventBus();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            bus.Publish(new UserRequest($"create file {tempDir} with content hello"));
            var rt = new UtilityAi.Utils.Runtime(bus, 0);

            var proposals = module.Propose(rt).ToList();
            Assert.Single(proposals);

            await proposals[0].Act(CancellationToken.None);

            var response = bus.GetOrDefault<AiResponse>();
            Assert.NotNull(response);
            Assert.Contains("Failed to create file", response.Text);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ParseFileRequest_ParsesWithContentSyntax()
    {
        var (path, content) = FileCreationModule.ParseFileRequest("create file notes.md with content some text");
        Assert.Equal("notes.md", path);
        Assert.Equal("some text", content);
    }

    [Fact]
    public void ParseFileRequest_FallbackWhenNoKeyword()
    {
        var (path, content) = FileCreationModule.ParseFileRequest("write data output.txt");
        Assert.False(string.IsNullOrEmpty(path));
    }

    [Fact]
    public void Propose_HasPositiveUtility()
    {
        var module = new FileCreationModule();
        var bus = new EventBus();
        bus.Publish(new UserRequest("create file test.txt with content hello"));
        var rt = new UtilityAi.Utils.Runtime(bus, 0);

        var proposals = module.Propose(rt).ToList();
        Assert.Single(proposals);
        Assert.True(proposals[0].Utility(rt) > 0);
    }
}
