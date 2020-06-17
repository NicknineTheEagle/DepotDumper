using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Linq;

using SteamKit2;
using System.Collections.Generic;
using System.Threading.Tasks;

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

            StreamWriter sw = new StreamWriter( string.Format( "{0}_steam.keys", user ) );
            sw.AutoFlush = true;
            StreamWriter sw2 = new StreamWriter( string.Format( "{0}_steam.apps", user ) );
            sw2.AutoFlush = true;
            StreamWriter sw3 = new StreamWriter( string.Format( "{0}_steam.pkgs", user ) );
            sw3.AutoFlush = true;

            Config.SuppliedPassword = password;
            AccountSettingsStore.LoadFromFile( "xxx" );

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

            Console.WriteLine( "Getting licenses..." );
            steam3.WaitUntilCallback( () => { }, () => { return steam3.Licenses != null; } );
            IEnumerable<uint> licenseQuery = steam3.Licenses.Select( x => x.PackageID ).Distinct();

            steam3.RequestPackageInfo( licenseQuery );

            var apps = new List<uint>();

            // Collect all apps user owns.
            foreach ( var license in licenseQuery )
            {
                SteamApps.PICSProductInfoCallback.PICSProductInfo package;
                if ( steam3.PackageInfo.TryGetValue( license, out package ) && package != null )
                {
                    var token = steam3.PackageTokens.ContainsKey( license ) ? steam3.PackageTokens[license] : 0;
                    sw3.WriteLine( "{0};{1}", license, token );

                    foreach ( uint appId in package.KeyValues["appids"].Children.Select( x => x.AsUnsignedInteger() ) )
                    {
                        if ( apps.Contains( appId ) )
                            continue;

                        apps.Add( appId );
                    }
                }
            }

            // Fetch AppInfo for all apps.
            steam3.RequestAppInfoList( apps );

            var depots = new List<uint>();

            // Go through all apps and get keys for each of of their depots
            foreach ( var appId in apps )
            {
                SteamApps.PICSProductInfoCallback.PICSProductInfo app;
                if ( !steam3.AppInfo.TryGetValue( appId, out app ) || app == null )
                {
                    continue;
                }

                KeyValue appinfo = app.KeyValues;
                KeyValue depotInfo = appinfo.Children.Where( c => c.Name == "depots" ).FirstOrDefault();

                if ( !steam3.AppTokens.ContainsKey( appId ) )
                    continue;

                sw2.WriteLine( "{0};{1}", appId, steam3.AppTokens[appId] );

                if ( depotInfo == null )
                    continue;

                foreach ( var depotSection in depotInfo.Children )
                {
                    uint id = uint.MaxValue;

                    if ( !uint.TryParse( depotSection.Name, out id ) || id == uint.MaxValue )
                        continue;

                    if ( depots.Contains( id ) )
                        continue;

                    // Skip empty depots.
                    if ( depotSection["manifests"] == KeyValue.Invalid )
                        continue;

                    // Skip depots user doesn't own.
                    bool isOwned = false;
                    foreach ( var license in licenseQuery )
                    {
                        SteamApps.PICSProductInfoCallback.PICSProductInfo package;
                        if ( steam3.PackageInfo.TryGetValue( license, out package ) && package != null )
                        {
                            if ( package.KeyValues["depotids"].Children.Any( child => child.AsUnsignedInteger() == id ) )
                            {
                                isOwned = true;
                            }
                        }
                    }

                    if ( !isOwned )
                        continue;

                    steam3.RequestDepotKey( id, appId );
                    if ( steam3.DepotKeys.ContainsKey( id ) )
                    {
                        sw.WriteLine( "{0};{1}", id, string.Concat( steam3.DepotKeys[id].Select( b => b.ToString( "X2" ) ).ToArray() ) );
                        depots.Add( id );
                    }
                }
            }

            sw.Close();
            sw2.Close();
            sw3.Close();

            steam3.TryWaitForLoginKey();
            steam3.Disconnect();

            return 0;
        }
    }
}
