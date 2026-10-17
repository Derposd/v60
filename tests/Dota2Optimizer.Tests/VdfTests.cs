using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Vdf;

namespace Dota2Optimizer.Tests;

public class VdfTests
{
    private const string LibraryFolders = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"apps"
        		{
        			"228980"		"249205025"
        		}
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        		"apps"
        		{
        			"570"		"41234567890"
        		}
        	}
        }
        """;

    [Fact]
    public void Parses_nested_objects_and_escaped_paths()
    {
        var root = VdfParser.Parse(LibraryFolders);
        Assert.Equal("libraryfolders", root.Key);
        Assert.Equal(@"C:\Program Files (x86)\Steam", root.Path("0")!.ValueOf("path"));
        Assert.Equal(@"D:\SteamLibrary", root["1"]!.ValueOf("path"));
        Assert.Equal("41234567890", root.Path("1", "apps", "570")!.Value);
    }

    [Fact]
    public void Keys_are_case_insensitive()
    {
        var root = VdfParser.Parse("\"A\" { \"LaunchOptions\" \"-novid\" }");
        Assert.Equal("-novid", root.ValueOf("launchoptions"));
    }

    [Fact]
    public void Ignores_comments_conditionals_and_bom()
    {
        var text = "\uFEFF// comment\n\"root\"\n{\n  \"a\" \"1\" [$WIN32]\n  // another\n  b 2\n}\n";
        var root = VdfParser.Parse(text);
        Assert.Equal("1", root.ValueOf("a"));
        Assert.Equal("2", root.ValueOf("b"));
    }

    [Fact]
    public void Roundtrip_preserves_values_with_quotes_and_backslashes()
    {
        var root = new VdfNode("root");
        root.SetValue("LaunchOptions", "-novid +exec \"my cfg\" C:\\x");
        var reparsed = VdfParser.Parse(root.Serialize());
        Assert.Equal("-novid +exec \"my cfg\" C:\\x", reparsed.ValueOf("LaunchOptions"));
    }

    [Fact]
    public void Unknown_escape_keeps_backslash()
    {
        var root = VdfParser.Parse("\"r\" { \"path\" \"C:\\Games\\Steam\" }");
        Assert.Equal(@"C:\Games\Steam", root.ValueOf("path"));
    }

    [Theory]
    [InlineData("\"r\" { \"a\" \"1\"")]
    [InlineData("\"r\" { \"a\" }")]
    [InlineData("\"r\" { \"a\" \"unterminated }")]
    public void Malformed_input_throws_parse_exception(string text)
    {
        Assert.Throws<VdfParseException>(() => VdfParser.Parse(text));
    }

    [Fact]
    public void Old_libraryfolders_format_is_supported()
    {
        var root = VdfParser.Parse("\"LibraryFolders\" { \"TimeNextStatsReport\" \"1\" \"1\" \"E:\\\\Games\" }");
        Assert.Equal(@"E:\Games", root.ValueOf("1"));
    }

    private const string LocalConfig = """
        "UserLocalConfigStore"
        {
        	"Software"
        	{
        		"Valve"
        		{
        			"Steam"
        			{
        				"apps"
        				{
        					"570"
        					{
        						"LastPlayed"		"1700000000"
        						"LaunchOptions"		"-novid -threads 8"
        					}
        					"730"
        					{
        						"LaunchOptions"		"-high"
        					}
        				}
        			}
        		}
        	}
        	"friends"
        	{
        		"x"		"y"
        	}
        }
        """;

    [Fact]
    public void LocalConfig_reads_and_writes_only_dota_launch_options()
    {
        Assert.Equal("-novid -threads 8", LocalConfigEditor.GetLaunchOptions(LocalConfig));

        var updated = LocalConfigEditor.SetLaunchOptions(LocalConfig, "-novid +fps_max 144");
        Assert.Equal("-novid +fps_max 144", LocalConfigEditor.GetLaunchOptions(updated));

        var root = VdfParser.Parse(updated);
        Assert.Equal("-high", root.Path("Software", "Valve", "Steam", "apps", "730")!.ValueOf("LaunchOptions"));
        Assert.Equal("1700000000", root.Path("Software", "Valve", "Steam", "apps", "570")!.ValueOf("LastPlayed"));
        Assert.Equal("y", root.Path("friends")!.ValueOf("x"));
    }

    [Fact]
    public void LocalConfig_creates_missing_app_node()
    {
        var text = "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { } } } }";
        var updated = LocalConfigEditor.SetLaunchOptions(text, "-novid");
        Assert.Equal("-novid", LocalConfigEditor.GetLaunchOptions(updated));
    }

    [Fact]
    public void LocalConfig_supports_capitalised_Apps_key()
    {
        var text = "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"Apps\" { \"570\" { \"LaunchOptions\" \"-console\" } } } } } }";
        Assert.Equal("-console", LocalConfigEditor.GetLaunchOptions(text));
        var updated = LocalConfigEditor.SetLaunchOptions(text, "-novid");
        Assert.Equal("-novid", LocalConfigEditor.GetLaunchOptions(updated));
        Assert.DoesNotContain("\"apps\"", updated); // no duplicate lower-case node
    }
}
