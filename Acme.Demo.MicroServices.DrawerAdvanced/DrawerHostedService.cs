// -----------------------------------------------------------------------
//  <copyright file="DrawerHostedService.cs" company="Acme">
//  Copyright (c) Acme. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

namespace Acme.Demo.MicroServices.DrawerAdvanced
{
    using System;
    using System.Drawing;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Acme.Demo.MicroService;

    using Azure.Messaging.ServiceBus;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    public class DrawerHostedService : IHostedService
    {
        private static readonly Random Dice = new();
        private readonly IConfiguration configuration;
        private readonly FileRepository fileRepository;
        private readonly ILogger logger;
        private ServiceBusReceiver receiver;
        private ServiceBusSender sender;

        public DrawerHostedService(IConfiguration configuration, FileRepository fileRepository, ILogger<DrawerHostedService> logger)
        {
            this.configuration = configuration;
            this.fileRepository = fileRepository;
            this.logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Task.Run(() => this.DoWork(), cancellationToken);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.receiver.DisposeAsync();
        }

        private int ComputeNextColor(int previous)
        {
            if (previous == -1)
            {
                return Dice.Next(0, 256);
            }

            return Dice.Next(Math.Max(0, previous - 10), Math.Min(256, previous + 10));
        }

        private async Task DoWork()
        {
            var receiverOptions = new ServiceBusReceiverOptions
            {
                PrefetchCount = 10,
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.None
            };

            this.receiver = new ServiceBusClient(this.configuration["ServiceBusConnectionStrings:Mentor"]).CreateReceiver("Mentor", receiverOptions);
            this.sender = new ServiceBusClient(this.configuration["ServiceBusConnectionStrings:Drawer"]).CreateSender("Drawer");

            await foreach (var message in this.receiver.ReceiveMessagesAsync())
            {
                #region 1 - Read and Complete message

                // Console.WriteLine(message.Body.ToObjectFromJson<PictureRequest>());
                // await this.receiver.CompleteMessageAsync(message);

                #endregion

                #region 2 - Read, Process and Complete message

                // var pictureRequest = message.Body.ToObjectFromJson<PictureRequest>();
                // this.DrawRandomImage(pictureRequest);
                // await this.receiver.CompleteMessageAsync(message);

                #endregion

                #region 3 - Read, Process and Complete or Abandon message

                var pictureRequest = message.Body.ToObjectFromJson<PictureRequest>();
                try
                {
                    this.logger.LogInformation($"Start drawing a {pictureRequest.PictureType}");
                    switch (pictureRequest.PictureType)
                    {
                        case PictureType.Advanced:
                            await this.DrawAdvancedImage(pictureRequest);
                            this.logger.LogInformation($"End drawing a {pictureRequest.PictureType}");
                            await this.receiver.CompleteMessageAsync(message);
                            break;
                        case PictureType.Random:
                            await this.DrawRandomImage(pictureRequest);
                            this.logger.LogInformation($"End drawing a {pictureRequest.PictureType}");
                            await this.receiver.CompleteMessageAsync(message);
                            break;
                        default:
                            await this.receiver.AbandonMessageAsync(message);
                            break;
                    }
                }
                catch (Exception e)
                {
                    await this.receiver.AbandonMessageAsync(message);
                    this.logger.LogError(e, "Cannot draw the image");
                }

                #endregion
            }
        }

        private async Task DrawAdvancedImage(PictureRequest pictureRequest)
        {
            int previousA = -1, previousR = -1, previousG = -1, previousB = -1;

            using var bitmap = new Bitmap(pictureRequest.Height, pictureRequest.Width);

            for (var x = 0; x < pictureRequest.Height; x++)
            for (var y = 0; y < pictureRequest.Width; y++)
            {
                previousA = this.ComputeNextColor(previousA);
                previousR = this.ComputeNextColor(previousR);
                previousG = this.ComputeNextColor(previousG);
                previousB = this.ComputeNextColor(previousB);

                var color = Color.FromArgb(previousA, previousR, previousG, previousB);
                bitmap.SetPixel(x, y, color);
            }

            var imageName = $"{pictureRequest.PictureType}-{Guid.NewGuid()}";
            this.fileRepository.SaveBitmap(imageName, bitmap);

            await this.SendPictureDone(imageName);
        }

        private async Task DrawRandomImage(PictureRequest pictureRequest)
        {
            using var bitmap = new Bitmap(pictureRequest.Height, pictureRequest.Width);

            for (var x = 0; x < pictureRequest.Height; x++)
            for (var y = 0; y < pictureRequest.Width; y++)
            {
                var color = Color.FromArgb(Dice.Next(0, 256), Dice.Next(0, 256), Dice.Next(0, 256), Dice.Next(0, 256));
                bitmap.SetPixel(x, y, color);
            }

            var imageName = $"{pictureRequest.PictureType}-{Guid.NewGuid()}";
            this.fileRepository.SaveBitmap(imageName, bitmap);

            await this.SendPictureDone(imageName);
        }

        private async Task SendPictureDone(string imageName)
        {
            var pictureDone = new PictureDone
            {
                ImageName = imageName
            };

            var message = new ServiceBusMessage();
            message.ContentType = "application/json";
            message.Body = new BinaryData(pictureDone);
            await this.sender.SendMessageAsync(message);
        }
    }
}