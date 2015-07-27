using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace MvcApplication1.Models
{
    public struct DSRCommand
    {
        public String deviceId { get; set; }
        public struct Command
        {
            public String commandName { get; set; }
            public struct Parameter
            {
                public String name { get; set; }
                public string value { get; set; }
            }
            public Parameter[] parameters;
        }
        public Command command;
    }


    public class ViewContext
    {
        public List<DSRCommand> history { get; set; }
        public String deviceIdForLogRequest { get; set; }
    }

    public interface IDataContextAbstract
    {
        void NewCommand(JObject command);
        JObject GetCommand(String deviceId);
        List<JObject> GetHistory(String deviceId);
    }



}