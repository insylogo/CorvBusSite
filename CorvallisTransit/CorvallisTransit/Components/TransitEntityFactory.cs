using CorvallisTransit.Components;
using CorvallisTransit.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace CorvallisTransit.Components
{
    public static class TransitEntityFactory
    {
        private static object locker = new object();
        private static List<BusRoute> routes;
        private static DateTime expires;


        /// <summary>
        /// Gets the routes from a route pattern obtained from xml.
        /// </summary>
        /// <param name="xmlPattern">The XML pattern.</param>
        /// <returns>All of the bus routes, with their associated stops.</returns>
        public static List<BusRoute> PatternToBusRoutes(RoutePattern xmlPattern)
        {
            IEnumerable<RoutePatternProjectRoute> xmlRoutes = xmlPattern.Project.Route;
            List<BusRoute> routeModels = new List<BusRoute>();
            List<Task> threads = new List<Task>();

            // first, do we need to get new route details?
            // only download the route info if either we don't have any
            // or we have passed their specified 'expiration' date
            if (routes == null || !routes.Any() || DateTime.Now >= expires)
                UpdateRouteInformation(xmlPattern, xmlRoutes, routeModels, threads);
            else
            {
                // otherwise, lets get a temporary list from our current routes
                routeModels = routes.ToList();
            }

            // update all the etas for all the stops for all the routes
            foreach (var route in routeModels)
            {
                foreach (var stop in route.Stops.OrderBy(s => s, TransitComparers.TransitComparer))
                {
                    // since we're threading, more closure
                    var stopClosure = stop;
                    threads.Add(Task.Run(() =>
                    {
                        UpdateStopEta(stopClosure);
                    }));
                }
                route.UpdatedSuccessfully = Task.WaitAll(threads.ToArray(), 40000);


                // we have a lot of stops, use the comparer to organize them by eta/position
                route.Stops = route.Stops.Distinct(TransitComparers.TransitComparer)
                                         .OrderBy(s => s, TransitComparers.TransitComparer)
                                         .ToList();
            }


            routes = routeModels.OrderBy(rm => rm.RouteNo).ToList();

            return routes;
        }

        /// <summary>
        /// Updates the stop eta.
        /// </summary>
        /// <param name="stopClosure">The stop closure.</param>
        private static void UpdateStopEta(BusRouteStop stopClosure)
        {
            var stopEtaDetails = TransitClient.GetPlatformEta(stopClosure.StopModel.StopTag);
            var platform = stopEtaDetails.GetPlatform(stopClosure);

            // if we don't have the platform, set Eta to 0, same if we don't have the detail
            if (platform != null)
            {
                var detail = platform.Route.FirstOrDefault(rt => rt.RouteNo == stopClosure.RouteModel.RouteNo);
                if (detail != null)
                {
                    stopClosure.Eta = detail.Destination.Trip.ETA;
                }
                else
                {
                    stopClosure.Eta = 0;
                }
            }
            else
            {
                stopClosure.Eta = 0;
            }
        }

        /// <summary>
        /// Updates the route information.
        /// </summary>
        /// <param name="xmlPattern">The XML pattern.</param>
        /// <param name="xmlRoutes">The XML routes.</param>
        /// <param name="routeModels">The route models.</param>
        /// <param name="threads">The threads.</param>
        private static void UpdateRouteInformation(RoutePattern xmlPattern, IEnumerable<RoutePatternProjectRoute> xmlRoutes, List<BusRoute> routeModels, List<Task> threads)
        {
            // update the expiration date for the route information
            expires = DateTime.Parse(xmlPattern.Content.Expires);
            foreach (var route in xmlRoutes.GroupBy(r => r.RouteNo))
            {
                // routestops are the association object between their individual stops and multiple routes
                List<BusRouteStop> stopAssociations = new List<BusRouteStop>();


                BusRoute model = new BusRoute()
                {
                    RouteNo = route.Key,
                    RouteTimeWarning = false,
                    Stops = stopAssociations
                };


                var pattern = route.SelectMany(r => r.Destination)
                                   .Select(d => d.Pattern.Where(p => p.Name.Equals(route.Key, StringComparison.CurrentCultureIgnoreCase)))
                                   .SelectMany(pt => pt.SelectMany(pl => pl.Platform.Select(p => p)));

                int stopCount = 0;

                foreach (var platform in pattern)
                {
                    // we need to capture the current platform because in this foreach loop it will change the object in the thread
                    // when we change the pointer
                    var stopClosure = platform;


                    // build this stop, they call them 'platforms
                    BusStop stopModel = new BusStop()
                    {
                        Address = stopClosure.Name,
                        StopNumber = stopClosure.PlatformNo,
                        StopTag = stopClosure.PlatformTag

                    };

                    // this is the association their system uses, ugh
                    BusRouteStop routeAssociation = new BusRouteStop()
                    {
                        RouteModel = model,
                        StopModel = stopModel,
                        StopPosition = stopCount++
                    };

                    threads.Add(Task.Run(() =>
                    {
                        GetPlatformGps(stopAssociations, stopClosure, stopModel, routeAssociation);
                    }));

                }

                // we'll give up after 20 seconds of trying to get all the gps details
                Task.WaitAll(threads.ToArray(), TransitConstants.ThreadTimeout);

                // order our stops using our custom comparer, logic inside!
                model.Stops = model.Stops.Distinct(TransitComparers.TransitComparer).ToList();

                routeModels.Add(model);
            }
        }

        /// <summary>
        /// Gets the platform GPS.
        /// </summary>
        /// <param name="stopAssociations">The stop associations.</param>
        /// <param name="stopClosure">The stop closure.</param>
        /// <param name="stopModel">The stop model.</param>
        /// <param name="routeAssociation">The route association.</param>
        private static void GetPlatformGps(List<BusRouteStop> stopAssociations, Platform stopClosure, BusStop stopModel, BusRouteStop routeAssociation)
        {
            // get the platform gps position
            var platformDetails = TransitClient.GetPlatform(stopClosure.PlatformTag);

            if (platformDetails.Position != null)
            {
                stopModel.Latitude = platformDetails.Position.Lat;
                stopModel.Longitude = platformDetails.Position.Long;
            }
            lock (locker)
            {
                stopAssociations.Add(routeAssociation);
            }
        }
    }
}