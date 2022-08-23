﻿using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronosShared.Authentication;
using MareSynchronosShared.Data;
using MareSynchronosShared.Metrics;
using MareSynchronosShared.Models;
using MareSynchronosShared.Protos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MareSynchronosServer.Hubs
{
    public partial class MareHub : Hub
    {
        private readonly MetricsService.MetricsServiceClient _metricsClient;
        private readonly AuthService.AuthServiceClient _authServiceClient;
        private readonly FileService.FileServiceClient _fileServiceClient;
        private readonly SystemInfoService _systemInfoService;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor contextAccessor;
        private readonly ILogger<MareHub> _logger;
        private readonly MareDbContext _dbContext;
        public MareHub(MetricsService.MetricsServiceClient metricsClient, AuthService.AuthServiceClient authServiceClient, FileService.FileServiceClient fileServiceClient,
            MareDbContext mareDbContext, ILogger<MareHub> logger, SystemInfoService systemInfoService, IConfiguration configuration, IHttpContextAccessor contextAccessor)
        {
            _metricsClient = metricsClient;
            _authServiceClient = authServiceClient;
            _fileServiceClient = fileServiceClient;
            _systemInfoService = systemInfoService;
            _configuration = configuration;
            this.contextAccessor = contextAccessor;
            _logger = logger;
            _dbContext = mareDbContext;
        }

        [HubMethodName(Api.InvokeHeartbeat)]
        [Authorize(AuthenticationSchemes = SecretKeyGrpcAuthenticationHandler.AuthScheme)]
        public async Task<ConnectionDto> Heartbeat(string characterIdentification)
        {
            await _metricsClient.IncreaseCounterAsync(new() { CounterName = MetricsAPI.CounterInitializedConnections, Value = 1 }).ConfigureAwait(false);

            var userId = Context.User!.Claims.SingleOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

            _logger.LogInformation("Connection from {userId}, CI: {characterIdentification}", userId, characterIdentification);

            await Clients.Caller.SendAsync(Api.OnUpdateSystemInfo, _systemInfoService.SystemInfoDto).ConfigureAwait(false);

            var isBanned = await _dbContext.BannedUsers.AsNoTracking().AnyAsync(u => u.CharacterIdentification == characterIdentification).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(userId) && !isBanned && !string.IsNullOrEmpty(characterIdentification))
            {
                var user = (await _dbContext.Users.SingleAsync(u => u.UID == userId).ConfigureAwait(false));
                if (!string.IsNullOrEmpty(user.CharacterIdentification) && characterIdentification != user.CharacterIdentification)
                {
                    return new ConnectionDto()
                    {
                        ServerVersion = Api.Version
                    };
                }
                else if (string.IsNullOrEmpty(user.CharacterIdentification))
                {
                    await _metricsClient.IncGaugeAsync(new GaugeRequest() { GaugeName = MetricsAPI.GaugeAuthorizedConnections, Value = 1 }).ConfigureAwait(false);
                }

                user.LastLoggedIn = DateTime.UtcNow;
                user.CharacterIdentification = characterIdentification;
                await _dbContext.SaveChangesAsync().ConfigureAwait(false);
                return new ConnectionDto
                {
                    ServerVersion = Api.Version,
                    UID = userId,
                    IsModerator = user.IsModerator,
                    IsAdmin = user.IsAdmin
                };
            }

            return new ConnectionDto()
            {
                ServerVersion = Api.Version
            };
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Connection from {ip}", contextAccessor.GetIpAddress());
            await _metricsClient.IncGaugeAsync(new GaugeRequest() { GaugeName = MetricsAPI.GaugeConnections, Value = 1 }).ConfigureAwait(false);
            await base.OnConnectedAsync().ConfigureAwait(false);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            await _metricsClient.DecGaugeAsync(new GaugeRequest() { GaugeName = MetricsAPI.GaugeConnections, Value = 1 }).ConfigureAwait(false);

            var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.UID == AuthenticatedUserId).ConfigureAwait(false);
            if (user != null && !string.IsNullOrEmpty(user.CharacterIdentification))
            {
                await _metricsClient.DecGaugeAsync(new GaugeRequest() { GaugeName = MetricsAPI.GaugeAuthorizedConnections, Value = 1 }).ConfigureAwait(false);

                _logger.LogInformation("Disconnect from {id}", AuthenticatedUserId);

                var query =
                    from userToOther in _dbContext.ClientPairs
                    join otherToUser in _dbContext.ClientPairs
                        on new
                        {
                            user = userToOther.UserUID,
                            other = userToOther.OtherUserUID

                        } equals new
                        {
                            user = otherToUser.OtherUserUID,
                            other = otherToUser.UserUID
                        }
                    where
                        userToOther.UserUID == user.UID
                        && !userToOther.IsPaused
                        && !otherToUser.IsPaused
                    select otherToUser.UserUID;
                var otherEntries = await query.ToListAsync().ConfigureAwait(false);

                await Clients.Users(otherEntries).SendAsync(Api.OnUserRemoveOnlinePairedPlayer, user.CharacterIdentification).ConfigureAwait(false);

                _dbContext.RemoveRange(_dbContext.Files.Where(f => !f.Uploaded && f.UploaderUID == user.UID));

                user.CharacterIdentification = null;
                await _dbContext.SaveChangesAsync().ConfigureAwait(false);
            }

            await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
        }

        protected string AuthenticatedUserId => Context.User?.Claims?.SingleOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "Unknown";

        protected async Task<User> GetAuthenticatedUserUntrackedAsync()
        {
            return await _dbContext.Users.AsNoTrackingWithIdentityResolution().SingleAsync(u => u.UID == AuthenticatedUserId).ConfigureAwait(false);
        }
    }
}
