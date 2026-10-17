using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Tests;

public class SteamLocatorTests
{
    [Fact]
    public void Finds_dota_in_secondary_library_and_account_files()
    {
        var fs = new InMemoryFileSystem();
        var reg = new InMemoryRegistry();
        reg.SetValue(RegHive.CurrentUser, @"Software\Valve\Steam", "SteamPath", RegValue.Str("c:/program files (x86)/steam"));
        const string steam = @"c:\program files (x86)\steam";
        fs.WriteAllText(steam + @"\steamapps\libraryfolders.vdf",
            "\"libraryfolders\" { \"0\" { \"path\" \"c:\\\\program files (x86)\\\\steam\" } \"1\" { \"path\" \"D:\\\\SteamLibrary\" } }");
        fs.WriteAllText(@"D:\SteamLibrary\steamapps\appmanifest_570.acf", "\"AppState\" { \"appid\" \"570\" \"installdir\" \"dota 2 beta\" \"buildid\" \"123\" }");
        fs.WriteAllText(@"D:\SteamLibrary\steamapps\common\dota 2 beta\game\dota\cfg\video.txt", "\"config\" { }");
        fs.WriteAllText(steam + @"\config\loginusers.vdf",
            "\"users\" { \"76561197960265729\" { \"MostRecent\" \"0\" } \"76561198000000000\" { \"MostRecent\" \"1\" } }");
        fs.WriteAllText(steam + @"\userdata\39734272\config\localconfig.vdf", "\"UserLocalConfigStore\" { }");
        fs.WriteAllText(steam + @"\userdata\39734272\570\remote\cfg\video.txt", "\"config\" { }");

        var info = new SteamLocator(fs, reg).Locate(name => name == "steam");

        Assert.True(info.DotaFound);
        Assert.Equal("123", info.DotaBuildId);
        Assert.Equal("39734272", info.ActiveAccountId); // 76561198000000000 - 76561197960265728
        Assert.Equal(2, info.Libraries.Count);
        Assert.Equal(2, info.VideoConfigPaths.Count);
        Assert.Single(info.CloudCfgDirs);
        Assert.NotNull(info.LocalConfigPath);
        Assert.True(info.SteamRunning);
        Assert.False(info.DotaRunning);
        Assert.EndsWith("dota2.exe", info.DotaExe);
    }

    [Fact]
    public void No_steam_returns_empty_info()
    {
        var info = new SteamLocator(new InMemoryFileSystem(), new InMemoryRegistry()).Locate();
        Assert.Null(info.SteamPath);
        Assert.False(info.DotaFound);
    }
}
