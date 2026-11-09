using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
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
        IQQEventDispatcher dispatcher,
        IBindCodeService bindCodes) : ControllerBase
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
            EventDispatchResult result = await dispatcher.DispatchAsync(payload, QQEventSource.Webhook, cancellationToken);
            return result.Status switch
            {
                EventDispatchStatus.Invalid => BadRequest(result.Reason),
                // 失败时返回 5xx，让平台重推；占位记录已撤销，重推可重新处理。
                EventDispatchStatus.Failed => StatusCode(StatusCodes.Status500InternalServerError, result.Reason),
                _ => Ok()
            };
        }

        [RequireApiKey]
        [HttpPost("thirdparty")]
        public async Task<IActionResult> ThirdParty(
            [FromBody] ThirdPartyMessage? message,
            [FromServices] RainBotService rainBot,
            CancellationToken cancellationToken)
        {
            if (message is null) return Ok("");

            // 第三方消息自报的 openid 只用于回显，身份一律以 ApiKey 关联的 ClaimsPrincipal 为准。
            message.FunGameUID = CurrentUserId();
            message.Principal = HttpContext.User;
            OtherData data = new() { RequestUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}" };
            bool handled = await rainBot.Handler(message, data, cancellationToken);
            return Ok(!handled || message.IsCompleted ? message.Result : "");
        }

        private long CurrentUserId()
            => long.TryParse(HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out long userId) ? userId : 0;

        [RequireApiKey]
        [HttpPost("bind")]
        public async Task<IActionResult> Bind([FromBody] BindRequest? request, CancellationToken cancellationToken)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.OpenId))
            {
                return BadRequest(new { error = "需要 code 与 openId。" });
            }

            ExternalIdentity identity = new(
                string.IsNullOrWhiteSpace(request.Provider) ? "qq" : request.Provider.Trim(),
                request.OpenId.Trim(),
                string.IsNullOrWhiteSpace(request.Scene) ? "c2c" : request.Scene.Trim());
            BindRedeemResult result = await bindCodes.RedeemAsync(request.Code.Trim(), identity, cancellationToken);
            logger.LogInformation("绑定码兑换：Status={Status}, UserId={UserId}, Provider={Provider}",
                result.Status, result.UserId, identity.Provider);

            return result.Status switch
            {
                BindRedeemStatus.Success => Ok(new { bound = true, userId = result.UserId }),
                BindRedeemStatus.Conflict => Conflict(new { error = result.Reason }),
                BindRedeemStatus.InvalidUser => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = result.Reason }),
                _ => BadRequest(new { error = result.Reason })
            };
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
                Timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                FunGameUID = CurrentUserId(),
                Principal = HttpContext.User
            };
            OtherData data = new() { RequestUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}" };
            bool handled = await rainBot.Handler(message, data, cancellationToken);
            return Ok(!handled || message.IsCompleted ? message.Result : "");
        }
    }
}
