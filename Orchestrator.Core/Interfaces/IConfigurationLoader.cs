using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;

namespace Orchestrator.Core.Interfaces
{
    public interface IConfigurationLoader
    {
        void Load(IConfiguration configuration);
    }
}