DepotDownloader
===============

Mass depot key dumper utilizing the SteamKit2 library. Supports .NET Core 3.1

Resulting files:

**{username}_appnames.txt**
* Contains names of all apps and depots owned by the account.

**{username}_apps.txt**
* Contains app tokens in "appId;appToken" format.

**{username}_keys.txt**
* Contains depot keys in "depotId;depotKey" format.

**{username}_pkgs.txt**
* Contains package tokens in "pkgId;pkgToken" format.

Optional parameters:
* -app \<#> - dump keys for a specific app.
* -skip-unreleased - skip apps that don't have "released" status, add this if you are a dev or a tester and don't want to accidentally leak an unreleased game.
