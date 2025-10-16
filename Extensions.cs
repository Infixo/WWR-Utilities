#pragma warning disable CA1416 // Validate platform compatibility

using Microsoft.Xna.Framework;
using STM.Data.Entities;
using STM.GameWorld;
using STM.GameWorld.Users;
using STMG.Engine;
using static System.Formats.Asn1.AsnWriter;

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


    // Get all passengers waiting in the city, similar to CityUser.GetPassengers(CityUser destination) but returns all cities
    public static Dictionary<CityUser, int> GetAllPassengers(this CityUser city)
    {
        Dictionary<CityUser, int> travellers = [];

        // helper
        void RegisterTravellers(CityUser cityUser, int people)
        {
            if (!travellers.ContainsKey(cityUser))
                travellers[cityUser] = 0;
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
            if (dest.People != 0)
                RegisterTravellers(dest.Destination.User, dest.People);
        }

        // review returning ones
        for (int j = 0; j < city.Returns.Count; j++)
        {
            ReturnDestination dest = city.Returns[j];
            if (dest.Ready != 0)
                RegisterTravellers(dest.Home.User, dest.Ready);
        }

        return travellers;
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

        // Check if this is city on the line itself
        if (connection.Instructions.Contains(direction))
        {
            CounterGetLine0++;
            return true;
        }

        // Analyze existing direct connections from any company
        for (int i3 = 0; i3 < from.Routes.Count; i3++)
        {
            if (from.Routes[i3].Instructions.Contains(direction))
            {
                Line _line7 = scene.Session.Companies[from.Routes[i3].Vehicle.Company].Line_manager.GetLine(from.Routes[i3].Vehicle);
                CounterGetLine1++;
                if (_line7 == connection) return true;
            }
        }

        // Analyze existing indirect connections
        for (int n = 0; n < from.Indirect.Count; n++)
        {
            if (from.Indirect[n].Destination != direction.City)
            {
                continue;
            }
            for (int l2 = 0; l2 < from.Routes.Count; l2++)
            {
                if (!from.Routes[l2].Instructions.Contains(from.Indirect[n].Next.User))
                {
                    continue;
                }
                Line _line3 = scene.Session.Companies[from.Routes[l2].Vehicle.Company].Line_manager.GetLine(from.Routes[l2].Vehicle);
                CounterGetLine2++;
                if (_line3 == connection) return true;
            }
        }

        // Search for indirect connections from any company
        PathSearchData _data = PathSearch.GetData();
        _data.Open.Add(new PathSearchNode(from.City, 0, PathSearchNode.GetDistanceCost(from.City, direction.City), 0, 0));
        CityPath _route = PathSearch.Get(from.City, direction.City, _data, int.MaxValue, scene, 0);
        CounterGetPath++;
        if (_route.Path != null)
        {
            //for (int m = 0; m < from.Routes.Count; m++)
            //{
            if (/*from.Routes[m]*/connection.Instructions.Contains(_route.Path[1].User))
            {
                //Line _line4 = scene.Session.Companies[from.Routes[m].Vehicle.Company].Line_manager.GetLine(from.Routes[m].Vehicle);
                //CounterGetLine3++;
                //if (_line4 == connection)
                return true;
            }
            //}
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
}


// Line extensions
public static class Line_Extensions
{
    public static long GetWaiting(this Line line, CityUser city)
    {
        long waiting = 0;
        Dictionary<CityUser, int> passengers = city.GetAllPassengers();
        foreach (var destination in passengers)
        {
            if (city.IsConnectedTo(destination.Key, line))
            {
                waiting += destination.Value;
            }
        }
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
        CityUser[] cities = line.Instructions.Cities; // for readability
        if (cities.Length < 2)
        {
            return 0.0; //  there is no route yet
        }

        static double GetDistance(CityUser a, CityUser b, byte vehicle_type)
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

        double distance = 0.0;
        for (int i = 1; i < cities.Length; i++)
        {
            double _dist = GetDistance(cities[i - 1], cities[i], line.Vehicle_type);
            distance += _dist;
        }
        if (line.Instructions.Cyclic)
        {
            double _dist = GetDistance(cities[^1], cities[0], line.Vehicle_type);
            distance += _dist;
        }
        return distance;
    }
}

#pragma warning restore CA1416 // Validate platform compatibility
