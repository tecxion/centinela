using Centinela.Core.Platform;

namespace Centinela.Core.Tests;

public class PlatformFoldersTests
{
    static string Special(Environment.SpecialFolder f) => f switch
    {
        Environment.SpecialFolder.ApplicationData => @"C:\Users\ana\AppData\Roaming",
        Environment.SpecialFolder.MyVideos => @"C:\Users\ana\Videos",
        Environment.SpecialFolder.MyPictures => @"C:\Users\ana\Pictures",
        Environment.SpecialFolder.MyDocuments => @"C:\Users\ana\Documents",
        _ => "",
    };

    static string? NoEnv(string name) => null;

    [Fact]
    public void Windows_uses_the_known_folders()
    {
        var f = PlatformFolders.For(OsKind.Windows, @"C:\Users\ana", NoEnv, Special, null);
        Assert.Equal(new UserFolders(Path.Combine(@"C:\Users\ana\AppData\Roaming", "Centinela"),
            @"C:\Users\ana\Videos", @"C:\Users\ana\Pictures", @"C:\Users\ana\Documents"), f);
    }

    [Fact]
    public void MacOS_uses_application_support_and_movies()
    {
        var f = PlatformFolders.For(OsKind.MacOS, "/Users/ana", NoEnv, Special, null);
        Assert.Equal(new UserFolders("/Users/ana/Library/Application Support/Centinela",
            "/Users/ana/Movies", "/Users/ana/Pictures", "/Users/ana/Documents"), f);
    }

    [Fact]
    public void Linux_defaults_to_xdg_locations()
    {
        var f = PlatformFolders.For(OsKind.Linux, "/home/ana", NoEnv, Special, null);
        Assert.Equal(new UserFolders("/home/ana/.local/share/centinela",
            "/home/ana/Videos", "/home/ana/Pictures", "/home/ana/Documents"), f);
    }

    [Fact]
    public void Linux_honors_xdg_data_home_and_localized_user_dirs()
    {
        const string userDirs = """
            # This file is written by xdg-user-dirs-update
            XDG_DOCUMENTS_DIR="$HOME/Documentos"
            XDG_PICTURES_DIR="$HOME/Imágenes"
            XDG_VIDEOS_DIR="/media/datos/Vídeos"
            """;
        var f = PlatformFolders.For(OsKind.Linux, "/home/ana",
            name => name == "XDG_DATA_HOME" ? "/home/ana/.datos" : null, Special, userDirs);
        Assert.Equal(new UserFolders("/home/ana/.datos/centinela",
            "/media/datos/Vídeos", "/home/ana/Imágenes", "/home/ana/Documentos"), f);
    }

    [Fact]
    public void Linux_ignores_a_user_dir_that_points_at_home_itself()
    {
        // xdg-user-dirs sets a folder to $HOME when the user disabled it.
        var f = PlatformFolders.For(OsKind.Linux, "/home/ana", NoEnv, Special, "XDG_VIDEOS_DIR=\"$HOME/\"");
        Assert.Equal("/home/ana/Videos", f.Videos);
    }

    [Fact]
    public void Linux_ignores_a_relative_xdg_data_home()
    {
        var f = PlatformFolders.For(OsKind.Linux, "/home/ana", name => name == "XDG_DATA_HOME" ? "datos" : null, Special, null);
        Assert.Equal("/home/ana/.local/share/centinela", f.Data);
    }

    [Fact]
    public void Linux_drops_the_trailing_slash_of_xdg_data_home()
    {
        var f = PlatformFolders.For(OsKind.Linux, "/home/ana", name => name == "XDG_DATA_HOME" ? "/home/ana/.datos/" : null,
            Special, null);
        Assert.Equal("/home/ana/.datos/centinela", f.Data);
    }

    [Fact]
    public void Linux_ignores_relative_user_dirs()
    {
        const string userDirs = """
            XDG_VIDEOS_DIR="Vídeos"
            XDG_PICTURES_DIR="$HOME/Imágenes"
            """;
        var f = PlatformFolders.For(OsKind.Linux, "/home/ana", NoEnv, Special, userDirs);
        Assert.Equal("/home/ana/Videos", f.Videos);
        Assert.Equal("/home/ana/Imágenes", f.Pictures);
    }
}
