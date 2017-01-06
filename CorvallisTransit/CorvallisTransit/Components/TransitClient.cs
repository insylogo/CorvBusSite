using CorvallisTransit.Models;
using CorvallisTransit.Components;
using GoogleMaps.LocationServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace CorvallisTransit.Components
{
    /// <summary>
    /// Static client that downloads and returns deserialized route and platform details.
    /// </summary>
    public static class TransitClient
    {
        private const int PLATFORM_WARNING_CUTOFF = 4;

        public static object StopsLocker { get; set; }
        private static DateTime expires = DateTime.Now;
        private static List<BusRoute> routes;
        private static object processRunningLock = new object();
        
        public static bool IsRunning { get; set; }

        static TransitClient()
        {
            IsRunning = false;
            StopsLocker = new object();
        }


        public delegate void OnRouteUpdate(BusRoute route);
        public static event OnRouteUpdate UpdateRoute;

        /// <summary>
        /// Gets the routes.
        /// </summary>
        /// <value>
        /// The routes.
        /// </value>
        public static List<BusRoute> Routes
        {
            get
            {
                return routes;
            }
        }

        /// <summary>
        /// Gets the stops, irrespective of route.
        /// </summary>
        /// <value>
        /// The stops.
        /// </value>
        public static List<BusRouteStop> Stops 
        {
            get 
            {
                return Routes.SelectMany(rt => rt.Stops).ToList();
            }
        }

        public static void InitializeAndUpdate()
        {             
            var tempRoutes = TransitEntityFactory.PatternToBusRoutes(Pattern);

            CheckForWarnings(tempRoutes);
            routes = tempRoutes;
            foreach (var route in routes)
            {
                if (UpdateRoute != null)
                {
                    UpdateRoute(route);
                }
                
            }
        }

        /// <summary>
        /// Checks for warnings.
        /// </summary>
        /// <param name="tempRoutes">The temporary routes used before we assign them to our routes variable.</param>
        private static void CheckForWarnings(List<BusRoute> tempRoutes)
        {
            foreach (var route in tempRoutes)
            {

                var platforms = route.Stops;
                var laterPlatforms = platforms.OrderBy(pt => pt.StopPosition).Skip(1).ToList();
                var laterPlatformsWithEta = laterPlatforms.SkipWhile(p => p.Eta == 0);
                if (laterPlatforms.IndexOf(laterPlatformsWithEta.FirstOrDefault()) > 0 && laterPlatformsWithEta.Where(p => p.Eta > 0).All(p => p.Eta > PLATFORM_WARNING_CUTOFF))
                {
                    route.RouteTimeWarning = true;
                }
                else
                {
                    route.RouteTimeWarning = false;
                }

            }
        }


        /// <summary>
        /// Gets the platform eta details.
        /// </summary>
        /// <param name="platformTag">The platform tag.</param>
        /// <returns></returns>
        public static RoutePosition GetPlatformEta(string platformTag)
        {
            if (string.IsNullOrWhiteSpace(platformTag))
            {
                return null;
            }

            return TransitDownloader.GetPlatformEta(platformTag);
        }


        /// <summary>
        /// Gets the platform details.
        /// </summary>
        /// <param name="platform">The platform.</param>
        /// <returns></returns>
        private static RoutePosition GetPlatformDetails(Platform platform)
        {
            return GetPlatformEta(platform.PlatformTag);
        }



        /// <summary>
        /// Downloads the RoutePattern from the CorvallisTransit site, deserializes and returns
        /// the object structure as generated from xsd.exe
        /// </summary>
        /// <value>
        /// The route's platform pattern structure.
        /// </value>
        private static RoutePattern Pattern
        {
            get
            {
                return TransitDownloader.GetRoutePattern();
            }
        }

        public static void Stop()
        {
            IsRunning = false;        
        }

        public static PlatformsPlatform GetPlatform(string platformTag)
        {
            return TransitDownloader.GetPlatform(platformTag);
        }

        /// <summary>
        /// The transit client's downloader
        /// </summary>
        private static class TransitDownloader
        {
            private static T GetEntity<T>(Uri uri) where T: class
            {
                XmlSerializer serializer = new XmlSerializer(typeof(T));

                string url = uri.AbsoluteUri; // string.Format("http://www.corvallistransit.com/rtt/public/utility/file.aspx?contenttype=SQLXML&Name=Platform.rxml&PlatformTag={0}", platformTag);

                using (WebClient client = new WebClient())
                {
                    string s = client.DownloadString(url);

                    TextReader reader = new StringReader(s);
                    return serializer.Deserialize(reader) as T;
                }
            }

            internal static PlatformsPlatform GetPlatform(string platformTag)
            {
                Uri location = new Uri(string.Format("http://www.corvallistransit.com/rtt/public/utility/file.aspx?contenttype=SQLXML&Name=Platform.rxml&PlatformTag={0}", platformTag));
                Platforms platformSet = GetEntity<Platforms>(location);

                return platformSet.Stops.FirstOrDefault();
            }

            internal static RoutePattern GetRoutePattern()
            {
                XmlSerializer serializer = new XmlSerializer(typeof(RoutePattern));
                Uri url = new Uri("http://www.corvallistransit.com/rtt/public/utility/file.aspx?contenttype=SQLXML&Name=RoutePattern.rxml");

                return GetEntity<RoutePattern>(url);        
            }

            internal static RoutePosition GetPlatformEta(string platformTag)
            {
                Uri url = new Uri(string.Format("http://www.corvallistransit.com/rtt/public/utility/file.aspx?contenttype=SQLXML&Name=RoutePositionET.xml&PlatformTag={0}", platformTag));
                 
                return GetEntity<RoutePosition>(url);
            }
        }

        internal static void UpdateClients()
        {
            foreach (var route in Routes)
            {
                UpdateRoute(route);
            }
        }
    }
}
