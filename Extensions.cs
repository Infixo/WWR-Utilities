#pragma warning disable CA1416 // Validate platform compatibility

using STM.Data;
using STM.GameWorld;
using STM.GameWorld.Users;
using STM.UI;
using STM.UI.Floating;
using STMG.Engine;
using STMG.UI.Control;
using STVisual.Utility;

namespace Utilities;


public static class WorldwideRushExtensions
{
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


    public static long GetWaiting(this Line line)
    {
        long waiting = 0;
        foreach (CityUser city in line.Instructions.Cities)
        {
            Dictionary<CityUser, int> passengers = city.GetAllPassengers();
            foreach (var destination in passengers)
            {
                GrowArray<Line> tempLines = new();
                if (city.IsConnectedTo(destination.Key, line, tempLines))
                {
                    waiting += destination.Value;
                }
            }
        }
        return waiting;
    }


    public static bool IsConnectedTo(this CityUser from, CityUser direction, Line connection, GrowArray<Line> lines)//, GameScene scene)
    {
        bool isConnected = false;
        GameScene scene = (GameScene)GameEngine.Last.Main_scene;
        //ushort _company = scene.Session.Player;
        //GrowArray<Line> lines = new GrowArray<Line>();

        // Analyze existing direct connections
        if (from.Connections_hash.Contains(direction.City))
        {
            // this looks for player's routes
            /*
            for (int i4 = 0; i4 < from.Routes.Count; i4++)
            {
                if (from.Routes[i4].Vehicle.Company == _company && from.Routes[i4].Instructions.Contains(direction))
                {
                    Line _line6 = scene.Session.Companies[from.Routes[i4].Vehicle.Company].Line_manager.GetLine(from.Routes[i4].Vehicle);
                    if (_line6 != null)
                    {
                        lines.AddSingle(_line6);
                    }
                }
            }
            */
            //if (lines.Count > 0)
            //{
                //tooltip.AddBoldLabel(Localization.GetCity("direct_exists"), null, center: true, LabelPresets.Color_positive);
            //}
            //else
            //{
                //tooltip.AddBoldLabel(Localization.GetCity("direct_exists_other"), null, center: true, LabelPresets.Color_negative);
            //}
            // Find the connecting routes from any company
            for (int i3 = 0; i3 < from.Routes.Count; i3++)
            {
                if (from.Routes[i3].Instructions.Contains(direction))
                {
                    Line _line7 = scene.Session.Companies[from.Routes[i3].Vehicle.Company].Line_manager.GetLine(from.Routes[i3].Vehicle);
                    if (_line7 != null)
                    {
                        lines.AddSingle(_line7);
                    }
                    if (_line7 == connection)
                        isConnected = true;
                }
            }
        }
        if (isConnected) return true;

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
                if (_line3 != null)
                {
                    lines.AddSingle(_line3);
                    //if (from.Routes[l2].Vehicle.Company == _company)
                    //{
                    //_player = true;
                    //}
                }
                if (_line3 == connection)
                    isConnected = true;
            }
        }
        if (isConnected) return true;

        // Search for indirect connections
        //if (!isConnected)
        //{
        // searches for indirect connections from the player's lines - too narrow?
        // could be mixed connections too as long as the connecting line is the one we are analyzing
        //bool _player = false;
        PathSearchData _data = PathSearch.GetData();
        _data.Open.Add(new PathSearchNode(from.City, 0, PathSearchNode.GetDistanceCost(from.City, direction.City), 0, 0));
        CityPath _route = PathSearch.Get(from.City, direction.City, _data, int.MaxValue, scene);
        if (_route.Path != null)
        {
            for (int i2 = 0; i2 < from.Routes.Count; i2++)
            {
                if (!from.Routes[i2].Instructions.Contains(_route.Path[1].User))
                {
                    continue;
                }
                Line _line5 = scene.Session.Companies[from.Routes[i2].Vehicle.Company].Line_manager.GetLine(from.Routes[i2].Vehicle);
                if (_line5 != null)
                {
                    lines.AddSingle(_line5);
                    //if (from.Routes[i2].Vehicle.Company == _company)
                    //{
                    //_player = true;
                    //}
                }
                if (_line5 == connection)
                    isConnected = true;
            }
        }
        if (isConnected) return true;

        //if (lines.Count > 0)
        //{
        //tooltip.AddBoldLabel(Localization.GetCity(_player ? "indirect_exists" : "indirect_exists_other"), null, center: true, _player ? LabelPresets.Color_positive : LabelPresets.Color_negative);
        //}
        //}
        // is still not connected, then search for indirect connections from other companies
        //if (!isConnected)
        //{
        _data = PathSearch.GetData();
        _data.Open.Add(new PathSearchNode(from.City, 0, PathSearchNode.GetDistanceCost(from.City, direction.City), 0, 0));
        _route = PathSearch.Get(from.City, direction.City, _data, int.MaxValue, scene, 0);
        if (_route.Path != null)
        {
            for (int m = 0; m < from.Routes.Count; m++)
            {
                if (from.Routes[m].Instructions.Contains(_route.Path[1].User))
                {
                    Line _line4 = scene.Session.Companies[from.Routes[m].Vehicle.Company].Line_manager.GetLine(from.Routes[m].Vehicle);
                    if (_line4 != null)
                    {
                        lines.AddSingle(_line4);
                    }
                    if (_line4 == connection)
                        isConnected = true;
                }
            }
        }
        return isConnected;
    }
    //if (lines.Count > 0)
    //{
    //tooltip.AddBoldLabel(Localization.GetCity("overcrowded_exsists"), null, center: true, LabelPresets.Color_negative);
    //int _most = 1;
    //for (int l = 2; l < _route.Path.Length - 1; l++)
    //{
    //if (_route.Path[l].User.Accepts_indirect > _route.Path[_most].User.Accepts_indirect)
    //{
    //_most = l;
    //}
    //}
    /*
    ButtonItem _locate = ButtonPresets.TextBlack(ContentRectangle.Zero, _route.Path[_most].User.GetNameWithIcon(scene), scene.Engine);
    _locate.Control.horizontal_alignment = HorizontalAlignment.Center;
    _locate.Control.Size_local = new Vector2(_locate.Label.Size_local.X + (float)MainData.Size_button, MainData.Size_button);
    tooltip.AddContent(_locate.Control);
    _locate.Control.OnButtonPress += (Action)delegate
    {
        if (Scene.tracking == _route.Path[_most].User)
        {
            _route.Path[_most].User.Select(Scene, track: true);
        }
        else
        {
            Scene.tracking = _route.Path[_most].User;
        }
    };
    */
    //}
    //else
    //{
    //tooltip.AddBoldLabel(Localization.GetCity("no_valid_path"), null, center: true, LabelPresets.Color_negative);
    //}
    //}

}

#pragma warning restore CA1416 // Validate platform compatibility
