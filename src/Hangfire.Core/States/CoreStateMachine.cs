// This file is part of Hangfire.
// Copyright ?2013-2014 Sergey Odinokov.
// 
// Hangfire is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as 
// published by the Free Software Foundation, either version 3 
// of the License, or any later version.
// 
// Hangfire is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public 
// License along with Hangfire. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Hangfire.Annotations;
using Hangfire.Common;
using Hangfire.Dashboard.Resources;

namespace Hangfire.States
{
    internal class CoreStateMachine : IStateMachine
    {
        private readonly Func<JobStorage, StateHandlerCollection> _stateHandlersThunk;

        public CoreStateMachine()
            : this(GetStateHandlers)
        {
        }

        internal CoreStateMachine([NotNull] Func<JobStorage, StateHandlerCollection> stateHandlersThunk)
        {
            if (stateHandlersThunk == null) throw new ArgumentNullException(nameof(stateHandlersThunk));
            _stateHandlersThunk = stateHandlersThunk;
        }

        public IState ApplyState(ApplyStateContext context)
        {
            var handlers = _stateHandlersThunk(context.Storage);

            foreach (var handler in handlers.GetHandlers(context.OldStateName))
            {
                handler.Unapply(context, context.Transaction);
            }

            var jobName = GetJobName(context.BackgroundJob.Job);
            if (!string.IsNullOrEmpty(jobName) && context.NewState.Name.Equals("Succeeded"))
            {
                context.NewState.Reason = jobName;
            }
            context.Transaction.SetJobState(context.BackgroundJob.Id, context.NewState);

            foreach (var handler in handlers.GetHandlers(context.NewState.Name))
            {
                handler.Apply(context, context.Transaction);
            }

            if (context.NewState.IsFinal)
            {
                context.Transaction.ExpireJob(context.BackgroundJob.Id, context.JobExpirationTimeout);
            }
            else
            {
                context.Transaction.PersistJob(context.BackgroundJob.Id);
            }

            return context.NewState;
        }

        private static StateHandlerCollection GetStateHandlers(JobStorage storage)
        {
            var stateHandlers = new StateHandlerCollection();
            stateHandlers.AddRange(GlobalStateHandlers.Handlers);
            stateHandlers.AddRange(storage.GetStateHandlers());

            return stateHandlers;
        }
        private static string GetJobName(Job job)
        {

            if (job == null)
            {
                return Strings.Common_CannotFindTargetMethod;
            }

            var displayNameAttribute = job.Method.GetCustomAttribute(typeof(DisplayNameAttribute)) as DisplayNameAttribute;
            if (displayNameAttribute != null && displayNameAttribute.DisplayName != null)
            {
                try
                {
                    return String.Format(displayNameAttribute.DisplayName, job.Args.ToArray());
                }
                catch (FormatException)
                {
                    return displayNameAttribute.DisplayName;
                }
            }
            return job.ToString();
        }
    }
}