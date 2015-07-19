using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace MvcApplication1
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                name: null,
                url: "command/{deviceId}/{timeout}",
                defaults: new { controller = "DSRWebService", action = "Command", deviceId = UrlParameter.Optional, timeout = UrlParameter.Optional }
            );

            routes.MapRoute(
                name: null,
                url: "{json}",
                defaults: new { controller = "DSRWebService", action = "NewCommand", json="sdsdsd" }
            );

            routes.MapRoute(
                name: "Default",
                url: "",
                defaults: new { controller = "DSRWebService", action = "NewCommand"}

            );
        }
    }
}