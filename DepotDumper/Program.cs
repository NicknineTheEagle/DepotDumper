using System;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Collections.Generic;

using SteamKit2;

namespace DepotDumper
{
    class Program
    {
        private static Steam3Session steam3;

        static int Main( string[] args )
        {
            Console.Write( "Username: " );
            string user = Console.ReadLine();
            string password;

            if ( !string.IsNullOrWhiteSpace( user ) )
            {
                Console.Write( "Password: " );
                if ( Console.IsInputRedirected )
                {
                    password = Console.ReadLine();
                }
                else
                {
                    // Avoid console echoing of password
                    password = Util.ReadPassword();
                    Console.WriteLine();
                }
            }
            else
            {
                // Login anonymously.
                user = null;
                password = null;
            }

            Config.SuppliedPassword = password;
            AccountSettingsStore.LoadFromFile( "xxx" );

            Config.TargetAppId = GetParameter<uint>( args, "-app", uint.MaxValue );
            Config.SkipUnreleased = HasParameter( args, "-skip-unreleased" );

            steam3 = new Steam3Session(
               new SteamUser.LogOnDetails()
               {
                   Username = user,
                   Password = password,
                   ShouldRememberPassword = false,
                   LoginID = 0x534B32, // "SK2"
               }
            );

            var steam3Credentials = steam3.WaitForCredentials();

            if ( !steam3Credentials.IsValid )
            {
                Console.WriteLine( "Unable to get steam3 credentials." );
                return 1;
            }

            IEnumerable<uint> licenseQuery;
            if ( steam3.steamUser.SteamID.AccountType == EAccountType.AnonUser )
            {
                licenseQuery = new List<uint>() { 17906 };
            }
            else
            {
                Console.WriteLine( "Getting licenses..." );
                steam3.WaitUntilCallback( () => { }, () => { return steam3.Licenses != null; } );
                licenseQuery = steam3.Licenses.Select( x => x.PackageID ).Distinct();
            }

            steam3.RequestPackageInfo( licenseQuery );

            if ( Config.TargetAppId == uint.MaxValue )
            {
                string filenameUser = ( steam3.steamUser.SteamID.AccountType != EAccountType.AnonUser ) ? user : "anon";

                StreamWriter sw_pkgs = new StreamWriter( string.Format( "{0}_pkgs.txt", filenameUser ) );
                sw_pkgs.AutoFlush = true;

                // Collect all apps user owns.
                var apps = new List<uint>();

                foreach ( var license in licenseQuery )
                {
                    SteamApps.PICSProductInfoCallback.PICSProductInfo package;
                    if ( steam3.PackageInfo.TryGetValue( license, out package ) && package != null )
                    {
                        var token = steam3.PackageTokens.ContainsKey( license ) ? steam3.PackageTokens[license] : 0;
                        sw_pkgs.WriteLine( "{0};{1}", license, token );

                        List<KeyValue> packageApps = package.KeyValues["appids"].Children;
                        apps.AddRange( packageApps.Select( x => x.AsUnsignedInteger() ).Where( x => !apps.Contains( x ) ) );
                    }
                }

                sw_pkgs.Close();

                StreamWriter sw_apps = new StreamWriter( string.Format( "{0}_apps.txt", filenameUser ) );
                sw_apps.AutoFlush = true;
                StreamWriter sw_keys = new StreamWriter( string.Format( "{0}_keys.txt", filenameUser ) );
                sw_keys.AutoFlush = true;
                StreamWriter sw_appnames = new StreamWriter( string.Format( "{0}_appnames.txt", filenameUser ) );
                sw_appnames.AutoFlush = true;

                // Fetch AppInfo for all apps.
                steam3.RequestAppInfoList( apps );

                var depots = new List<uint>();

                // Go through all apps and get keys for each of their depots.
                foreach ( var appId in apps )
                {
                    DumpApp( appId, licenseQuery, sw_apps, sw_keys, sw_appnames, depots );
                }

                sw_apps.Close();
                sw_keys.Close();
                sw_appnames.Close();
            }
            else
            {
                steam3.RequestAppInfo( Config.TargetAppId );

                if ( steam3.AppTokens.ContainsKey( Config.TargetAppId ) )
                {
                    StreamWriter sw_apps = new StreamWriter( string.Format( "app_{0}_token.txt", Config.TargetAppId ) );
                    StreamWriter sw_keys = new StreamWriter( string.Format( "app_{0}_keys.txt", Config.TargetAppId ) );
                    StreamWriter sw_appnames = new StreamWriter( string.Format( "app_{0}_names.txt", Config.TargetAppId ) );

                    DumpApp( Config.TargetAppId, licenseQuery, sw_apps, sw_keys, sw_appnames, new List<uint>() );

                    sw_apps.Close();
                    sw_keys.Close();
                    sw_appnames.Close();
                }
            }

            steam3.TryWaitForLoginKey();
            steam3.Disconnect();

            return 0;
        }

        static bool DumpApp( uint appId, IEnumerable<uint> licenses,
            StreamWriter sw_apps, StreamWriter sw_keys, StreamWriter sw_appnames,
            List<uint> depots )
        {
            SteamApps.PICSProductInfoCallback.PICSProductInfo app;
            if ( !steam3.AppInfo.TryGetValue( appId, out app ) || app == null )
                return false;

            if ( !steam3.AppTokens.ContainsKey( appId ) )
                return false;

            KeyValue appInfo = app.KeyValues;
            KeyValue depotInfo = appInfo["depots"];

            if ( Config.SkipUnreleased &&
                appInfo["common"]["ReleaseState"] != KeyValue.Invalid &&
                appInfo["common"]["ReleaseState"].AsString() != "released" )
                return false;

            sw_apps.WriteLine( "{0};{1}", appId, steam3.AppTokens[appId] );
            sw_appnames.WriteLine( "{0} - {1}", appId, appInfo["common"]["name"].AsString() );

            if ( depotInfo == KeyValue.Invalid )
                return false;

            foreach ( var depotSection in depotInfo.Children )
            {
                uint depotId = uint.MaxValue;

                if ( !uint.TryParse( depotSection.Name, out depotId ) || depotId == uint.MaxValue )
                    continue;

                // Skip empty depots.
                if ( depotSection["manifests"] == KeyValue.Invalid )
                {
                    if ( depotSection["depotfromapp"] != KeyValue.Invalid )
                    {
                        uint otherAppId = depotSection["depotfromapp"].AsUnsignedInteger();
                        if ( otherAppId == appId )
                        {
                            // This shouldn't ever happen, but ya never know with Valve.
                            Console.WriteLine( "App {0}, Depot {1} has depotfromapp of {2}!",
                                appId, depotId, otherAppId );
                            continue;
                        }

                        steam3.RequestAppInfo( otherAppId );

                        SteamApps.PICSProductInfoCallback.PICSProductInfo otherApp;
                        if ( !steam3.AppInfo.TryGetValue( otherAppId, out otherApp ) || otherApp == null )
                            continue;

                        if ( otherApp.KeyValues["depots"][depotId.ToString()]["manifests"] == KeyValue.Invalid )
                            continue;
                    }
                    else
                    {
                        continue;
                    }
                }

                // Skip depots user doesn't own.
                bool isOwned = false;
                foreach ( var license in licenses )
                {
                    SteamApps.PICSProductInfoCallback.PICSProductInfo package;
                    if ( steam3.PackageInfo.TryGetValue( license, out package ) && package != null )
                    {
                        // Check app list, too, since owning an app with the same ID counts as owning the depot.
                        if ( package.KeyValues["depotids"].Children.Any( child => child.AsUnsignedInteger() == depotId ) ||
                            package.KeyValues["appids"].Children.Any( child => child.AsUnsignedInteger() == depotId ) )
                        {
                            isOwned = true;
                            break;
                        }
                    }
                }

                if ( !isOwned )
                    continue;

                steam3.RequestDepotKey( depotId, appId );

                byte[] depotKey;
                if ( steam3.DepotKeys.TryGetValue( depotId, out depotKey ) )
                {
                    if ( !depots.Contains( depotId ) )
                    {
                        sw_keys.WriteLine( "{0};{1}", depotId, string.Concat( depotKey.Select( b => b.ToString( "X2" ) ).ToArray() ) );
                        depots.Add( depotId );
                    }

                    sw_appnames.WriteLine( "\t{0} - {1}", depotId, depotSection["name"].AsString() );

                    if ( depotSection["manifests"] != KeyValue.Invalid )
                    {
                        foreach ( var branch in depotSection["manifests"].Children )
                        {
                            sw_appnames.WriteLine( "\t\t{0} - {1}", branch.Name, branch.AsUnsignedLong() );
                        }
                    }
                }
            }

            return true;
        }

        static int IndexOfParam( string[] args, string param )
        {
            for ( int x = 0; x < args.Length; ++x )
            {
                if ( args[x].Equals( param, StringComparison.OrdinalIgnoreCase ) )
                    return x;
            }
            return -1;
        }

        static bool HasParameter( string[] args, string param )
        {
            return IndexOfParam( args, param ) > -1;
        }

        static T GetParameter<T>( string[] args, string param, T defaultValue = default( T ) )
        {
            int index = IndexOfParam( args, param );

            if ( index == -1 || index == ( args.Length - 1 ) )
                return defaultValue;

            string strParam = args[index + 1];

            var converter = TypeDescriptor.GetConverter( typeof( T ) );
            if ( converter != null )
            {
                return (T)converter.ConvertFromString( strParam );
            }

            return default( T );
        }
    }
}
