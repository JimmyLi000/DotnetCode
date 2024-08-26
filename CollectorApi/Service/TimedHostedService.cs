using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using CollectorApi.Tool;
using Timer = System.Timers.Timer;

namespace CollectorApi.Service
{
    public class TimedHostedService : IHostedService, IDisposable
    {
        private Timer _timer;
        private int timerNum = 0;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(1000);
            _timer.Elapsed += TimerElapsed;
            _timer.AutoReset = true;
            _timer.Enabled = true;
            return Task.CompletedTask;
        }


        private void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            timerNum++;
            int num = timerNum;

            // tool.asyncSnmpDataToFile();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Stop();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}