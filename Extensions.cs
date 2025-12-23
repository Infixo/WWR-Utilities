using Microsoft.Xna.Framework;
using STM.Data;
using STM.Data.Entities;
using STM.GameWorld;
using STM.GameWorld.Users;
using STMG.Engine;
using STVisual.Utility;
using System.Runtime.CompilerServices;

namespace Utilities;


public static class WorldwideRushExtensions
{
    // 20251114 There is Accepts_indirect calculated every frame in CityUser.StartUpdate
    // Doing some math shows that ratio = accepts_indirect * 5
    // 5 comes from 100/20 where 20 is hardcoded and means "city is full"
    // The treshold values are thus rescaled by 5
    public static Color OvercrowdedColor(this CityUser city, Color defColor)
    {
        //int ratio = city.GetTotalIndirect() * 100 / city.GetMaxIndirect();
        if (city.Accepts_indirect >= 30) return Color.DarkRed; // 150%
        else if (city.Accepts_indirect >= 20) return Color.Red; // 100%
        else if (city.Accepts_indirect >= 15) return Color.DarkOrange; // 75%
        else if (city.Accepts_indirect >= 10) return Color.Yellow; // 50%
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
    public static void GetAllPassengers(this CityUser city, Dictionary<CityUser, int> travellers, bool includeIndirect)
    {
        // helper
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void RegisterTravellers(CityUser cityUser, int people)
        {
            if (!travellers.TryAdd(cityUser, people))
                travellers[cityUser] += people;
        }

        // review destinations
        for (int d = 0; d < city.Destinations.Items.Count; d++)
        {
            CityDestination dest = city.Destinations.Items[d];
            if (dest.People > 0)
                RegisterTravellers(dest.Destination.User, dest.People);
        }

        // review indirect traffic
        if (includeIndirect)
            for (int i = 0; i < city.Indirect.Count; i++)
            {
                Passengers dest = city.Indirect.Items[i];
                if (dest.People > 0)
                    RegisterTravellers(dest.Destination.User, dest.People);
            }

        // review returning ones
        for (int r = 0; r < city.Returns.Count; r++)
        {
            ReturnDestination dest = city.Returns[r];
            if (dest.Ready > 0)
                RegisterTravellers(dest.Home.User, dest.Ready);
        }
    }


    // Counters to count method calls
    public static int CounterIsConn = 0; // counts IsConnectedTo
    public static int CounterGetLine0 = 0;
    public static int CounterGetLine1 = 0;
    public static int CounterGetLine2 = 0;
    public static int CounterGetLine3 = 0;
    public static int CounterGetPath = 0; // counts GetPath calls

    /// <summary>
    /// Checks if city <paramref name="from"/> is connected to a city 
    /// <paramref name="direction"/> via given line <paramref name="connection"/>.
    /// </summary>
    /// <param name="connection">The line used for connection.</param>
    /// <param name="from">The starting city.</param>
    /// <param name="direction">The target city.</param>
    /// <returns>True if connected</returns>
    public static bool Connects(this Line connection, CityUser from, CityUser direction, GrowArray<City[]> allPaths)
    {
        CounterIsConn++;

        // Check if this is city on the line itself
        if (connection.Instructions.Contains(from) && connection.Instructions.Contains(direction))
        {
            CounterGetLine0++;
            return true;
        }

        // 2025-11-02 Direct connections from other companies always take precedence over indirect ones
        // Review other lines and see if they connect destination
        HashSet<Route> reviewed = [connection.Instructions];
        for (int i = 0; i < from.Routes.Count; i++)
        {
            RouteInstance route = from.Routes[i];
            if (!reviewed.Contains(route.Instructions))
            {
                if (route.Instructions.Contains(direction))
                {
                    CounterGetLine0++;
                    return false;
                }
                reviewed.Add(route.Instructions);
            }
        }

        // 2025-11-02 Use already calculated and stored paths
        for (int i = 0; i < allPaths.Count; i++)
            if (allPaths[i].Contains(direction.City))
            {
                CounterGetLine2++;
                return true;
            }

        // Last resort - search for an indirect connections via PathSearch
        GameScene scene = (GameScene)GameEngine.Last.Main_scene;
        PathSearchData _data = PathSearch.GetData(from.City, direction.City, scene);
        // 2025-10-25 Patch 1.1.13 adjusted_start_cost added
        _data.Open.Add(new PathSearchNode(from.City, 0, 0, PathSearchNode.GetDistanceCost(from.City, direction.City), 0, 0));
        CityPath _route = PathSearch.Get(from.City, direction.City, _data, int.MaxValue, scene, overcrowd_factor: 0); // Last param if omitted ignores overcrowded lines!
        CounterGetPath++;
        if (_route.Path != null)
        {
            if (connection.Instructions.Contains(_route.Path[1].User))
            {
                CounterGetLine3++;
                return true;
            }
        }
        return false;
    }


    /// <summary>
    /// Compiles all stored paths into a single array to speed up later checks.
    /// </summary>
    /// <param name="Routes"></param>
    /// <param name="company"></param>
    /// <returns></returns>
    internal static GrowArray<City[]> GetAllStoredPaths(GrowArray<RouteInstance> Routes, ushort company = ushort.MaxValue)
    {
        GrowArray<City[]> allPaths = new();
        for (int i = 0; i < Routes.Count; i++)
        {
            if (company == ushort.MaxValue || Routes[i].Vehicle.Company == company)
            {
                GrowArray<CityPath> paths = Routes[i].Vehicle.GetPrivateField<GrowArray<CityPath>>("paths");
                for (int j = 0; j < paths.Count; j++)
                    if (paths[j].Path != null)
                        allPaths.AddSingle(paths[j].Path, (a, b) => a.SequenceEqual(b));
            }
        }
        return allPaths;
    }


    /// <summary>
    /// Checks if city is connected to another one by a company's route.
    /// </summary>
    /// <param name="from"></param>
    /// <param name="direction"></param>
    /// <param name="company"></param>
    /// <param name="scene"></param>
    /// <returns></returns>
    public static bool IsConnected(this CityUser from, CityUser direction, ushort company, GrowArray<City[]> allPaths, GameScene scene)
    {
        CounterIsConn++;

        // Direct connections
        if (from.Connections_hash.Contains(direction.City))
        {
            CounterGetLine0++;
            for (int i = 0; i < from.Routes.Count; i++)
                if (from.Routes[i].Vehicle.Company == company && from.Routes[i].Instructions.Contains(direction))
                    return true;
            // Another company has a direct route, so our indirect connection won't work anyway!
            return false;
        }

        // Indirect connections
        for (int i = 0; i < allPaths.Count; i++)
            if (allPaths[i].Contains(direction.City))
            {
                CounterGetLine2++;
                return true;
            }

        // Last resort - search for an indirect connections via PathSearch
        PathSearchData _data = PathSearch.GetData(from.City, direction.City, scene);
        // 2025-10-25 Patch 1.1.13 adjusted_start_cost added
        _data.Open.Add(new PathSearchNode(from.City, 0, 0, PathSearchNode.GetDistanceCost(from.City, direction.City), 0, 0));
        CityPath _route = PathSearch.Get(from.City, direction.City, _data, int.MaxValue, scene, overcrowd_factor: 0); // Last param if omitted ignores overcrowded lines!
        CounterGetPath++;
        if (_route.Path != null)
        {
            for (int i = 0; i < from.Routes.Count; i++)
                if (from.Routes[i].Vehicle.Company == company && from.Routes[i].Instructions.Contains(_route.Path[1].User))
                {
                    CounterGetLine3++;
                    return true;
                }
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


    /// <summary>
    /// Checks if the vehicle is better (higher tier or capacity) than the one. Checks for range for planes.
    /// </summary>
    /// <param name="original"></param>
    /// <param name="company"></param>
    /// <param name="next"></param>
    /// <param name="hub"></param>
    /// <param name="range"></param>
    /// <returns></returns>
    public static bool IsBetter(this VehicleBaseEntity original, Company company, VehicleBaseEntity next, Hub hub, int range)
    {
        if (next.Graphics.Entity == null || !next.CanBuy(company, hub.Longitude))
            return false;
        if (next is PlaneEntity _plane && _plane.Range < range)
            return false;
        if (original is TrainEntity _o && next is TrainEntity _n)
            if (_n.Tier > _o.Tier || (_n.Tier == _o.Tier && _n.Max_capacity > _o.Max_capacity))
                return true;
        if (next.Tier > original.Tier || (next.Tier == original.Tier && next.Capacity > original.Capacity))
            return true;
        return false;
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
    public static long GetWaiting(this Line line, CityUser city)
    {
        long waiting = 0;

        // 2025-11-02 Indirect passengers have already a route set, and will load ONLY when their next stop is served by the line
        // Source: VehiclePassengers.Load(RouteInstance, Passengers, Company)
        for (int i = 0; i < city.Indirect.Count; i++)
        {
            Passengers dest = city.Indirect.Items[i];
            if (dest.People > 0 && (line.Instructions.Contains(dest.Next.User) || line.Instructions.Contains(dest.Destination.User)))
            {
                waiting += dest.People;
                WorldwideRushExtensions.CounterGetLine1++;
            }
        }


        // 2025-11-02 Use already calculated and stored paths
        GrowArray<City[]> allPaths = WorldwideRushExtensions.GetAllStoredPaths(line.Routes);

        // Destinations and Returns - direct connections, stored paths and indirect new paths
        Dictionary<CityUser, int> passengers = [];
        city.GetAllPassengers(passengers, false);
        foreach (var destination in passengers)
            if (line.Connects(city, destination.Key, allPaths))
                waiting += destination.Value;

        return waiting;
    }


    public static long GetWaiting(this Line line)
    {
        long waiting = 0;
        foreach (CityUser city in line.Instructions.Cities)
            waiting += line.GetWaiting(city);
        return waiting;
    }


    // Calculate total distance, for cyclic routes adds last-first section
    // Distance between cities is not stored, it is calculated when needed :(
    public static double GetTotalDistance(this Line line)
    {
        return line.Instructions.GetTotalDistance(line.Vehicle_type);
    }

    // Calculate total distance, for cyclic routes adds last-first section
    // Distance between cities is not stored, it is calculated when needed :(
    public static double GetTotalDistance(this Route route, byte vehicle_type)
    {
        CityUser[] cities = route.Cities; // for readability
        if (cities.Length < 2)
        {
            return 0.0; //  there is no route yet
        }
        double distance = 0.0;
        for (int i = 1; i < cities.Length; i++)
        {
            double _dist = WorldwideRushExtensions.GetDistance(cities[i - 1], cities[i], vehicle_type);
            distance += _dist;
        }
        if (route.Cyclic)
        {
            double _dist = WorldwideRushExtensions.GetDistance(cities[^1], cities[0], vehicle_type);
            distance += _dist;
        }
        return distance;
    }
}
