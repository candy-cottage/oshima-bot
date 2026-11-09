using System.Threading.Channels;
using OshimaBot.Models;

namespace OshimaBot.Services
{
    public sealed class BotMessageQueue : IBotMessageSink
    {
        private readonly Channel<IBotMessage> _channel = Channel.CreateBounded<IBotMessage>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        public ValueTask QueueAsync(IBotMessage message, CancellationToken cancellationToken = default)
            => _channel.Writer.WriteAsync(message, cancellationToken);

        internal IAsyncEnumerable<IBotMessage> ReadAllAsync(CancellationToken cancellationToken)
            => _channel.Reader.ReadAllAsync(cancellationToken);

        internal void Complete() => _channel.Writer.TryComplete();
    }

    public sealed class BotMessageBackgroundService(
        BotMessageQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<BotMessageBackgroundService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (IBotMessage message in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using IServiceScope scope = scopeFactory.CreateScope();
                    RainBotService rainBot = scope.ServiceProvider.GetRequiredService<RainBotService>();
                    bool handled = await rainBot.Handler(message, new OtherData(), stoppingToken);
                    if (!handled)
                    {
                        logger.LogDebug("忽略未识别的机器人指令：{Command}", message.Detail);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "处理机器人消息失败：MessageId={MessageId}", message.Id);
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            queue.Complete();
            return base.StopAsync(cancellationToken);
        }
    }
}
