using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Linq;

using SteamKit2;
using System.Collections.Generic;

namespace DepotDumper
{
    class Program
    {

        public static StreamWriter sw;
        public static StreamWriter sw2;


        static void Main( string[] args )
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

            sw = new StreamWriter( string.Format( "{0}_steam.keys", user ) );
            sw.AutoFlush = true;
            sw2 = new StreamWriter( string.Format( "{0}_steam.appids", user ) );
            sw2.AutoFlush = true;

            Config.SuppliedPassword = password;
            AccountSettingsStore.LoadFromFile( "xxx" );

            var steam3 = new Steam3Session(
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
                return;
            }

            Console.WriteLine( "Getting licenses..." );
            steam3.WaitUntilCallback( () => { }, () => { return steam3.Licenses != null; } );

            IEnumerable<uint> licenseQuery;
            licenseQuery = steam3.Licenses.Select( x => x.PackageID ).Distinct();
            steam3.RequestPackageInfo( licenseQuery );

            foreach ( var license in licenseQuery )
            {
                SteamApps.PICSProductInfoCallback.PICSProductInfo package;
                if ( steam3.PackageInfo.TryGetValue( license, out package ) && package != null )
                {
                    //var availableDepots = package.KeyValues["depotids"].Children.Select(x => x.AsUnsignedInteger()).ToList();

                    foreach ( uint appId in package.KeyValues["appids"].Children.Select( x => x.AsUnsignedInteger() ) )
                    {
                        steam3.RequestAppInfo( appId );

                        SteamApps.PICSProductInfoCallback.PICSProductInfo app;
                        if ( !steam3.AppInfo.TryGetValue( appId, out app ) || app == null )
                        {
                            continue;
                        }

                        KeyValue appinfo = app.KeyValues;
                        KeyValue depots = appinfo.Children.Where( c => c.Name == "depots" ).FirstOrDefault();
                        KeyValue common = appinfo.Children.Where( c => c.Name == "common" ).FirstOrDefault();

                        if ( depots == null )
                            continue;

                        string appName = "** UNKNOWN **";
                        if ( common != null )
                        {
                            KeyValue nameKV = common.Children.Where( c => c.Name == "name" ).FirstOrDefault();
                            if ( nameKV != null )
                            {
                                appName = nameKV.AsString();
                            }
                        }

                        Console.WriteLine( "Got AppInfo for {0}: {1}", appId, appName );

                        sw2.WriteLine( "{0};{1}", appId, appName );

                        foreach ( var depotSection in depots.Children )
                        {
                            uint id = uint.MaxValue;

                            if ( !uint.TryParse( depotSection.Name, out id ) || id == uint.MaxValue )
                                continue;

                            if ( depotSection["manifests"] == KeyValue.Invalid )
                                continue;

                            // Can't check the package which the app is in since user's depots may be scattered across different packages.
                            steam3.RequestDepotKey( id, appId );
                            if ( steam3.DepotKeys.ContainsKey( id ) )
                            {
                                sw.WriteLine( "{0};{1}", id, string.Concat( steam3.DepotKeys[id].Select( b => b.ToString( "X2" ) ).ToArray() ) );
                            }
                            else
                            {
                                Console.WriteLine( "Failed to get a key for depot {0} of app {1}", id, appId );
                            }
                        }
                    }
                }
            }

            sw.Close();
            sw2.Close();
        }
    }
}
