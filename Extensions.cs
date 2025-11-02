#pragma warning disable CA1416 // Validate platform compatibility

using Microsoft.Xna.Framework;
using STM.Data;
using STM.Data.Entities;
using STM.GameWorld;
using STM.GameWorld.Users;
using STMG.Engine;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Utilities;


public static class WorldwideRushExtensions
{
    public static Color OvercrowdedColor(this CityUser city, Color defColor)
    {
        int ratio = city.GetTotalIndirect() * 100 / city.GetMaxIndirect();
        if (ratio > 150) return Color.DarkRed;
        else if (ratio > 100) return Color.Red;
        else if (ratio > 75) return Color.DarkOrange;
        else if (ratio > 50) return Color.Yellow;
        return defColor;
    }

    public static string GetVehicleTypeIcon(int vehicle_type)
    {
        switch (vehicle_type)
        {
            case 0: return "<!cicon_road_vehicle>";
            case 1: return "<!cicon_train>";
            case 2: return "<!cicon_plane>";
            case 3: return "<!cicon_ship>";
        }
        return "?";
    }

    public static string GetVehicleTypeIcon(string type_name)
    {
        switch (type_name)
        {
            case "road_vehicle": return "<!cicon_road_vehicle>";
            case "train": return "<!cicon_train>";
            case "plane": return "<!cicon_plane>";
            case "ship": return "<!cicon_ship>";
        }
        return "?";
    }

    public static string GetCountryName(this City city, GameScene scene)
    {
        return city.GetCountry(scene).Name.GetTranslation(Localization.Language);
    }

    public static string GetCountryName(this CityUser city, GameScene scene) => city.City.GetCountryName(scene);


    // Get all passengers waiting in the city, similar to CityUser.GetPassengers(CityUser destination) but returns all cities
    public static void GetAllPassengers(this CityUser city, Dictionary<CityUser, int> travellers)
    {
        //Dictionary<CityUser, int> travellers = [];

        // helper
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void RegisterTravellers(CityUser cityUser, int people)
        {
            //if (!travellers.ContainsKey(cityUser))
                //travellers[cityUser] = 0;
            if (!travellers.TryAdd(cityUser, people))
                travellers[cityUser] += people;
        }

        // review destinations
        for (int l = 0; l < city.Destinations.Items.Count; l++)
        {
            CityDestination dest = city.Destinations.Items[l];
            if (dest.People > 0)
                RegisterTravellers(dest.Destination.User, dest.People);
        }

        // review indirect traffic
        for (int k = 0; k < city.Indirect.Count; k++)
        {
            Passengers dest = city.Indirect.Items[k];
            if (dest.People > 0)
                RegisterTravellers(dest.Destination.User, dest.People);
        }

        // review returning ones
        for (int j = 0; j < city.Returns.Count; j++)
        {
            ReturnDestination dest = city.Returns[j];
            if (dest.Ready > 0)
                RegisterTravellers(dest.Home.User, dest.Ready);
        }

        //return travellers;
    }

    /// <summary>
    /// Calculates number of passengers waiting to go to one of the cities on the route.
    /// </summary>
    /// <param name="city"></param>
    /// <param name="route"></param>
    /// <returns></returns>
    public static long GetPassengersEx(this CityUser city, Route route)
    {
        long result = 0L;
        // Direct passengers - easy, the city must be on the route
        // Not so easy... must also check indirect connections!
        for (int k = 0; k < city.Destinations.Items.Count; k++)
        {
            // Easy scenario - direct connection to a city on the route
            if (route.Contains(city.Destinations.Items[k].Destination.User))
            {
                result += city.Destinations.Items[k].People;
            }
            else
            // The destination is off the route, so we will count it only if there is a connection from any other city on the route excluding the current one
            {
                foreach (CityUser otherCity in route.Cities)
                    if (otherCity != city && otherCity.Connections_hash.Contains(city.Destinations.Items[k].Destination))
                    {
                        result += city.Destinations.Items[k].People;
                    }
            }
        }
        // Indirect passengers - tricky, must check if connecting route is actually the one we're on
        for (int j = 0; j < city.Indirect.Count; j++)
        {
            if (/*ty.Indirect[j].Destination == destination.City ||*/route.Contains(city.Indirect[j].Next.User))
            {
                result += city.Indirect[j].People;
            }
        }
        // Returning passengers - same as direct, must account for indirect connection too
        for (int i = 0; i < city.Returns.Count; i++)
        {
            if (route.Contains(city.Returns[i].Home.User))
            {
                result += city.Returns[i].Ready;
            }
            else
            {
                foreach (CityUser otherCity in route.Cities)
                    if (otherCity != city && otherCity.Connections_hash.Contains(city.Returns[i].Home))
                    {
                        result += city.Returns[i].Ready;
                    }
            }
        }
        return result;
    }


    // Counters to count method calls
    public static int CounterIsConn = 0; // counts IsConnectedTo
    public static int CounterGetLine0 = 0;
    public static int CounterGetLine1 = 0;
    public static int CounterGetLine2 = 0;
    public static int CounterGetLine3 = 0;
    public static int CounterGetPath = 0; // counts GetPath calls

    /// <summary>
    /// Checks if city <paramref name="from"/> is connected to city 
    /// <paramref name="direction"/> via given line <paramref name="connection"/>.
    /// </summary>
    /// <param name="from">The starting city.</param>
    /// <param name="direction">The target city.</param>
    /// <param name="connection">The line used for connection.</param>
    /// <returns>True if connected</returns>
    public static bool IsConnectedTo(this CityUser from, CityUser direction, Line connection)
    {
        CounterIsConn++;
        GameScene scene = (GameScene)GameEngine.Last.Main_scene;

        // TEST TEST
        return from.ConnectsTo(direction);

        // Check if this is city on the line itself
        if (connection.Instructions.Contains(from) && connection.Instructions.Contains(direction))
        {
            CounterGetLine0++;
            return true;
        }

        // Analyze existing direct connections from any company
        // 2025-11-01 Performance analysis - GetLine is super inefficient
        // However, here we can reverse the search - since we only need to check a specific line!
        // If one RouteInstance from that line connects the cities then ALL do!
        /*
        for (int i3 = 0; i3 < from.Routes.Count; i3++)
        {
            if (from.Routes[i3].Instructions.Contains(direction))
            {
                Line _line7 = scene.Session.Companies[from.Routes[i3].Vehicle.Company].Line_manager.GetLine(from.Routes[i3].Vehicle); // [3622]
                CounterGetLine1++;
                if (_line7 == connection) return true;
            }
        }
        */

        // Analyze existing indirect connections
        for (int n = 0; n < from.Indirect.Count; n++)
        {
            if (from.Indirect[n].Destination == direction.City && connection.Instructions.Contains(from.Indirect[n].Next.User))
            {
                CounterGetLine2++;
                return true;
            }
            /*
            for (int l2 = 0; l2 < from.Routes.Count; l2++)
            {
                if (!from.Routes[l2].Instructions.Contains(from.Indirect[n].Next.User)) // [5064]
                {
                    continue;
                }
                // Similar trick as above - use reverse check!
                Line _line3 = scene.Session.Companies[from.Routes[l2].Vehicle.Company].Line_manager.GetLine(from.Routes[l2].Vehicle); // [15100]
                CounterGetLine2++;
                if (_line3 == connection) return true;
            }
            */
        }

        // 2025-11-01 Limit expensive searches
        if (!from.ConnectsTo(direction))
            return false;

        // Search for indirect connections from any company
        PathSearchData _data = PathSearch.GetData();
        // 2025-10-25 Patch 1.1.13 adjusted_start_cost added
        _data.Open.Add(new PathSearchNode(from.City, 0, 0, PathSearchNode.GetDistanceCost(from.City, direction.City), 0, 0));
        CityPath _route = PathSearch.Get(from.City, direction.City, _data, int.MaxValue, scene, 0); // [76200]
        CounterGetPath++;
        if (_route.Path != null)
        {
            //for (int m = 0; m < from.Routes.Count; m++)
            //{
            if (/*from.Routes[m]*/connection.Instructions.Contains(_route.Path[1].User))
            {
                //Line _line4 = scene.Session.Companies[from.Routes[m].Vehicle.Company].Line_manager.GetLine(from.Routes[m].Vehicle);
                CounterGetLine3++;
                //if (_line4 == connection)
                return true;
            }
            //}
        }
        return false;
    }


    public static bool ConnectsTo(this CityUser from, CityUser to)
    {
        // (1) Direct connection
        if (from.Connections_hash.Contains(to.City))
        {
            CounterGetLine0++;
            return true;
        }

        // (2) One-stop connection
        for (int i = 0; i < from.Connections.Count; i++)
            if (from.Connections[i].User.Connections_hash.Contains(to.City))
            {
                CounterGetLine1++;
                return true;
            }

        // (3) Two-stop connection
        for (int i = 0; i < from.Connections.Count; i++)
            for (int j = 0; j < from.Connections[i].User.Connections.Count; j++)
                if (from.Connections[i].User.Connections[j].User.Connections_hash.Contains(to.City))
                {
                    CounterGetLine2++;
                    return true;
                }

        // (4) Three-stop connection
        for (int i = 0; i < from.Connections.Count; i++)
            for (int j = 0; j < from.Connections[i].User.Connections.Count; j++)
                for (int k = 0; k < from.Connections[i].User.Connections[j].User.Connections.Count; k++)
                    if (from.Connections[i].User.Connections[j].User.Connections[k].User.Connections_hash.Contains(to.City))
                    {
                        CounterGetLine3++;
                        return true;
                    }

        return false;
    }


    /// <summary>
    /// Converts the value into the currency.
    /// </summary>
    /// <param name="entity">Scene currency</param>
    /// <param name="number">Value to convert</param>
    /// <returns></returns>
    public static decimal Convert(this CurrencyEntity entity, decimal number)
    {
        return Math.Ceiling((decimal)number * entity.Factor / 100m);
    }


    /// <summary>
    /// Get the line to which the vehicle belongs to.
    /// </summary>
    /// <param name="vehicle"></param>
    /// <returns></returns>
    public static Line? GetLine(this VehicleBaseUser vehicle, GameScene scene)
    {
        return scene.Session.Companies[vehicle.Company].Line_manager.GetLine(vehicle);
    }
    public static Line? GetLine(this VehicleBaseUser vehicle)
    {
        return vehicle.GetLine((GameScene)GameEngine.Last.Main_scene);
    }


    public static int GetDistance(CityUser a, CityUser b, string type_name)
    {
        // TODO: this could be cached later on to speed up the calculations
        return type_name switch
        {
            "road_vehicle" => RoadPathSearch.GetRoute(a, b).Distance,
            "train" => RoadPathSearch.GetRails(a, b).Distance,
            "plane" => (int)GameScene.GetDistance(a, b),
            "ship" => SeaPathSearch.GetRoute(a, b).Distance,
            _ => 0,
        };
    }

    public static double GetDistance(CityUser a, CityUser b, byte vehicle_type)
    {
        // TODO: this could be cached later on to speed up the calculations
        switch (vehicle_type)
        {
            case 0: return RoadPathSearch.GetRoute(a, b).Distance;
            case 1: return RoadPathSearch.GetRails(a, b).Distance;
            case 2: return GameScene.GetDistance(a, b);
            case 3: return SeaPathSearch.GetRoute(a, b).Distance;
        }
        return 0d;
    }

}


// Line extensions
public static class Line_Extensions
{
    public struct CityInfo
    {
        public int Level = 0;
        public bool Access = true;
        public CityInfo (int n, bool a) { Level = n; Access = a; }
        public override readonly string ToString() => $"{Level}-{Access}";
    };

    /// <summary>
    /// Adds to the set all cities directly connected with them
    /// </summary>
    /// <param name="cities"></param>
    private static void ExpandConnections(HashSet<City> cities, HashSet<City> processed)
    {
        HashSet<City> next = [];
        foreach (City city in cities.Except(processed))
        {
            next.UnionWith(city.User.Connections_hash);
            processed.Add(city);
        }
        cities.UnionWith(next); // Add to the original set
    }


    // 2025-11-01 New approach
    /// <summary>
    /// Returns number of passengers waiting to use the line in the set of cities.
    /// </summary>
    /// <param name="line"></param>
    /// <param name="cities"></param>
    /// <returns></returns>
    public static long GetWaiting(this Line line, City[] cities)
    {
        // Build a list of passengers - make sure we only process cities on the line
        Dictionary<CityUser, int> passengers = [];
        foreach(City city in cities.Where(c => line.Instructions.Contains(c.User)))
            city.User.GetAllPassengers(passengers);

        if (passengers.Count == 0) return 0; // safety

        // Build connections hash-set
        HashSet<City> connected = [.. line.Instructions.Cities.Select(c => c.City)];
        HashSet<City> processed = [];
        ExpandConnections(connected, processed); // this will add all cities connected ditectly to the line-cities
        ExpandConnections(connected, processed); // 1st hop
        ExpandConnections(connected, processed); // 2nd hop

        // Calculate waiting passengers
        long waiting = 0;
        foreach (var dest in passengers)
            if (connected.Contains(dest.Key.City))
                waiting += dest.Value;

        return waiting;
    }


    public static long GetWaiting(this Line line, CityUser city)
    {
        if (!line.Instructions.Contains(city)) return 0;

        // Build list of cities accessible via the line
        Dictionary<CityUser, CityInfo> network = [];
        network.Add(city, new CityInfo(0, false));
        foreach(CityUser c in line.Instructions.Cities)
            if (c != city) network.Add(c, new CityInfo(1, true));
        HashSet<Route> processed = [line.Instructions];

        // Determine cities that can be accessed via the line
        void ExpandConnections(int level)
        {
            Dictionary<CityUser, CityInfo> next = [];
            foreach (var pair in network.Where(x => x.Value.Level == level))
                for (int i = 0; i < pair.Key.Routes.Count; i++)
                {
                    Route route = pair.Key.Routes[i].Instructions;
                    if (!processed.Contains(route))
                    {
                        foreach (CityUser c in route.Cities)
                            next.TryAdd(c, new CityInfo(level+1, true));
                        processed.Add(route);
                    }
                }
            foreach (var pair in next) network.TryAdd(pair.Key, pair.Value);
        }
        ExpandConnections(1);
        ExpandConnections(2);
        ExpandConnections(3);

        // Remove cities that can be accessed more directly
        processed = [line.Instructions]; // reset
        void RemoveConnections(int level)
        {
            foreach (var pair in network.Where(x => x.Value.Level == level-1 && !x.Value.Access))
                for (int i = 0; i < pair.Key.Routes.Count; i++)
                {
                    Route route = pair.Key.Routes[i].Instructions;
                    if (!processed.Contains(route))
                    {
                        foreach (CityUser c in route.Cities)
                        {
                            if (network.ContainsKey(c) && level < network[c].Level) // this is better connection
                                    network[c] = new CityInfo(level, false);
                                // same level or worse - all other cities from here will be same or worse
                        }
                        processed.Add(route);
                    }
                } 
        }
        RemoveConnections(1);
        RemoveConnections(2);
        RemoveConnections(3);

        // Process passengers
        long waiting = 0;
        Dictionary<CityUser, int> passengers = [];
        city.GetAllPassengers(passengers);
        HashSet<CityUser> filtered = [.. network.Where(x => x.Value.Access).Select(x => x.Key)];
        foreach (var destination in passengers)
            if (filtered.Contains(destination.Key))
                waiting += destination.Value;
        
        return waiting;
    }


    public static long GetWaiting(this Line line)
    {
        //return line.GetWaiting([.. line.Instructions.Cities.Select(c => c.City)]);
        long waiting = 0;
        foreach (CityUser city in line.Instructions.Cities)
            waiting += line.GetWaiting(city);
        return waiting;
    }


    // Calculate total distance, for cyclic routes adds last-first section
    // Distance between cities is not stored, it is calculated when needed :(
    public static double GetTotalDistance(this Line line)
    {
        CityUser[] cities = line.Instructions.Cities; // for readability
        if (cities.Length < 2)
        {
            return 0.0; //  there is no route yet
        }
        double distance = 0.0;
        for (int i = 1; i < cities.Length; i++)
        {
            double _dist = WorldwideRushExtensions.GetDistance(cities[i - 1], cities[i], line.Vehicle_type);
            distance += _dist;
        }
        if (line.Instructions.Cyclic)
        {
            double _dist = WorldwideRushExtensions.GetDistance(cities[^1], cities[0], line.Vehicle_type);
            distance += _dist;
        }
        return distance;
    }
}

#pragma warning restore CA1416 // Validate platform compatibility
