using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OshimaBot.Models;
using OshimaBot.Security;
using OshimaBot.Services;

namespace OshimaBot.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public sealed class QQBotController(
        IOptions<QQBotOptions> options,
        ILogger<QQBotController> logger,
        BotMessageQueue queue,
        IMemoryCache memoryCache) : ControllerBase
    {
        private readonly QQBotOptions _options = options.Value;

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] Payload? payload, CancellationToken cancellationToken)
        {
            // WebSocket 为唯一运行模式；webhook 仅在 QQBot:EnableWebhook=true 时接受平台回调。
            if (!_options.EnableWebhook) return NotFound();
            if (payload is null) return BadRequest("Payload 格式无效。");

            try
            {
                return payload.Op switch
                {
                    13 => HandleValidation(payload),
                    // op=12 是 HTTP 回调模式的 ACK 回包；平台不会主动推送，HTTP 200 即视为确认。
                    12 => Ok(),
                    0 => await HandleEventAsync(payload, cancellationToken),
                    _ => Ok()
                };
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception, "QQ Webhook JSON 格式无效。");
                return BadRequest("无效的 JSON 格式。");
            }
        }

        private IActionResult HandleValidation(Payload payload)
        {
            if (string.IsNullOrWhiteSpace(_options.Secret))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "QQBot Secret 尚未配置。");
            }

            ValidationRequest? validation = payload.Data.Deserialize<ValidationRequest>();
            if (validation is null || string.IsNullOrWhiteSpace(validation.PlainToken) || string.IsNullOrWhiteSpace(validation.EventTs))
            {
                return BadRequest("无效的验证 Payload 格式。");
            }

            byte[] signature = WebhookSignatureVerifier.SignValidation(
                _options.Secret, validation.EventTs, validation.PlainToken);

            return Ok(new ValidationResponse
            {
                PlainToken = validation.PlainToken,
                Signature = Convert.ToHexString(signature).ToLowerInvariant()
            });
        }

        private async Task<IActionResult> HandleEventAsync(Payload payload, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(payload.Id))
            {
                string cacheKey = $"OshimaBot:QQBot:Event:{payload.Id}";
                if (memoryCache.TryGetValue(cacheKey, out _)) return Ok();
                memoryCache.Set(cacheKey, true, TimeSpan.FromMinutes(10));
            }

            IBotMessage? message = payload.EventType switch
            {
                "C2C_MESSAGE_CREATE" => payload.Data.Deserialize<C2CMessage>(),
                "GROUP_AT_MESSAGE_CREATE" => payload.Data.Deserialize<GroupAtMessage>(),
                "INTERACTION_CREATE" => payload.Data.Deserialize<InteractionEvent>(),
                _ => null
            };

            if (message is null)
            {
                if (payload.EventType is "C2C_MESSAGE_CREATE" or "GROUP_AT_MESSAGE_CREATE" or "INTERACTION_CREATE")
                {
                    return BadRequest("无效的消息事件数据。");
                }
                logger.LogDebug("忽略未定义的 QQ 事件：{EventType}", payload.EventType);
                return Ok();
            }

            if (string.IsNullOrWhiteSpace(message.OpenId) || string.IsNullOrWhiteSpace(message.Detail))
            {
                return Ok();
            }

            logger.LogInformation("收到 QQ {EventType} 消息：MessageId={MessageId}", payload.EventType, message.Id);
            await queue.QueueAsync(message, cancellationToken);
            return Ok();
        }

        [RequireApiKey]
        [HttpPost("thirdparty")]
        public async Task<IActionResult> ThirdParty(
            [FromBody] ThirdPartyMessage? message,
            [FromServices] RainBotService rainBot,
            CancellationToken cancellationToken)
        {
            if (message is null) return Ok("");

            OtherData data = new() { RequestUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}" };
            bool handled = await rainBot.Handler(message, data, cancellationToken);
            return Ok(!handled || message.IsCompleted ? message.Result : "");
        }

        [RequireApiKey]
        [HttpPost("thirdpartytest")]
        public async Task<IActionResult> ThirdPartyTest(
            [FromQuery] string msg,
            [FromQuery] string openID,
            [FromServices] RainBotService rainBot,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(msg)) return Ok("");

            ThirdPartyMessage message = new()
            {
                IsGroup = false,
                AuthorOpenId = openID,
                OpenId = openID,
                Detail = msg,
                Id = openID,
                Timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            OtherData data = new() { RequestUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}" };
            bool handled = await rainBot.Handler(message, data, cancellationToken);
            return Ok(!handled || message.IsCompleted ? message.Result : "");
        }
    }
}
