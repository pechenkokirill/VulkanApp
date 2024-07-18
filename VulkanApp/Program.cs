using GLFW;
using System.Reflection;
using System.Runtime.InteropServices;
using Vulkan;

namespace VulkanApp;

internal class VulkanApp
{
    private NativeWindow nativeWindow = null!;

    internal void Run()
    {
        if (!Glfw.Init()) throw new ApplicationException("Glfw not inited");

        Glfw.WindowHint(Hint.ClientApi, ClientApi.None);
        Glfw.WindowHint(Hint.Resizable, false);
        nativeWindow = new NativeWindow(640, 480, "Window");

        var extensions = GetRequierdInstanceExtensions().Append("VK_EXT_debug_report").ToArray();
        var layers = new string[] { "VK_LAYER_KHRONOS_validation" };

        var instance = new Instance(new InstanceCreateInfo
        {
            EnabledExtensionCount = (uint)extensions.Length,
            EnabledExtensionNames = extensions,
            EnabledLayerCount = (uint)extensions.Length,
            EnabledLayerNames = layers,
        });

        instance.EnableDebug(VulkanDebugCallback);

        var surface = CreateWindowSurface(instance, nativeWindow.Handle);
        var physicalDevices = instance.EnumeratePhysicalDevices();
        LogPhysicalDevices(physicalDevices);
        var physicalDevice = physicalDevices.First();
        var layerProperties = physicalDevice.EnumerateDeviceLayerProperties();
        var avaliableExtensions = physicalDevice.EnumerateDeviceExtensionProperties()
            .Select(x => x.ExtensionName)
            .Intersect(["VK_KHR_swapchain"])
            .ToArray();
        Console.WriteLine("Avaliable Extensions:");
        foreach (var extension in avaliableExtensions)
        {
            Console.WriteLine(extension);
        }

        var queueProperties = physicalDevice.GetQueueFamilyProperties();
        var graphicsQueueIndex = 0U;
        foreach (var familyProperties in queueProperties)
        {
            if (familyProperties.QueueFlags.HasFlag(QueueFlags.Graphics))
            {
                break;
            }

            graphicsQueueIndex++;
        }

        var device = physicalDevice.CreateDevice(new DeviceCreateInfo
        {
            EnabledExtensionCount = (uint)avaliableExtensions.Length,
            EnabledExtensionNames = avaliableExtensions,
            EnabledLayerCount = (uint)layers.Length,
            EnabledLayerNames = layers,
            QueueCreateInfoCount = 1,
            QueueCreateInfos = [new DeviceQueueCreateInfo {
                QueueCount = 1,
                QueueFamilyIndex = graphicsQueueIndex,
                QueuePriorities = [0.0f],
            }]
        });

        var queue = device.GetQueue(graphicsQueueIndex, 0);

        var surfaceCapabilities = physicalDevice.GetSurfaceCapabilitiesKHR(surface);
        var surfaceFormat = physicalDevice.GetSurfaceFormatsKHR(surface)[0];
        var buffersCount = 2U;

        var swapchain = device.CreateSwapchainKHR(new SwapchainCreateInfoKhr
        {
            Clipped = true,
            CompositeAlpha = surfaceCapabilities.SupportedCompositeAlpha,
            ImageArrayLayers = 1,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageFormat = surfaceFormat.Format,
            ImageExtent = surfaceCapabilities.CurrentExtent,
            ImageSharingMode = SharingMode.Exclusive,
            ImageUsage = ImageUsageFlags.ColorAttachment,
            MinImageCount = buffersCount,
            PresentMode = PresentModeKhr.Mailbox,
            PreTransform = surfaceCapabilities.CurrentTransform,
            Surface = surface,
            QueueFamilyIndexCount = 1,
            QueueFamilyIndices = [graphicsQueueIndex]
        });

        var swapchainImages = device.GetSwapchainImagesKHR(swapchain);
        var imageViews = new ImageView[swapchainImages.Length];
        for (int i = 0; i < swapchainImages.Length; i++)
        {
            imageViews[i] = device.CreateImageView(new ImageViewCreateInfo
            {
                Image = swapchainImages[i],
                Format = surfaceFormat.Format,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.Color,
                    BaseMipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                    LevelCount = 1,
                },
                ViewType = ImageViewType.View2D
            });
        }

        var renderPass = device.CreateRenderPass(new RenderPassCreateInfo
        {
            AttachmentCount = 1,
            Attachments = [new AttachmentDescription {
                Format = surfaceFormat.Format,
                FinalLayout = ImageLayout.PresentSrcKhr,
                InitialLayout = ImageLayout.Undefined,
                LoadOp = AttachmentLoadOp.Clear,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                Samples = SampleCountFlags.Count1,
                StoreOp = AttachmentStoreOp.Store
            }],
            DependencyCount = 1,
            Dependencies = [new SubpassDependency {
                SrcSubpass = 0,
                DstSubpass = 0,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutput,
                DstAccessMask = 0,
                SrcAccessMask = AccessFlags.ColorAttachmentWrite,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutput,
                DependencyFlags = DependencyFlags.ByRegion
            }],
            SubpassCount = 1,
            Subpasses = [new SubpassDescription {
                ColorAttachmentCount = 1,
                ColorAttachments = [new AttachmentReference {
                    Attachment = 0,
                    Layout = ImageLayout.ColorAttachmentOptimal
                }],
            }],
        });

        var frameBuffers = new Framebuffer[imageViews.Length];
        for (int i = 0; i < frameBuffers.Length; i++)
        {
            frameBuffers[i] = device.CreateFramebuffer(new FramebufferCreateInfo
            {
                AttachmentCount = 1,
                Attachments = [imageViews[i]],
                Layers = 1,
                Width = surfaceCapabilities.CurrentExtent.Width,
                Height = surfaceCapabilities.CurrentExtent.Height,
                RenderPass = renderPass
            });
        }

        var commandPool = device.CreateCommandPool(new CommandPoolCreateInfo
        {
            QueueFamilyIndex = graphicsQueueIndex,
            Flags = CommandPoolCreateFlags.ResetCommandBuffer
        });

        var commandBuffer = device.AllocateCommandBuffers(new CommandBufferAllocateInfo
        {
            CommandPool = commandPool,
            CommandBufferCount = 1,
            Level = CommandBufferLevel.Primary
        }).First();

        var semophore = device.CreateSemaphore(new SemaphoreCreateInfo());
        var renderSemophore = device.CreateSemaphore(new SemaphoreCreateInfo());
        var fence = device.CreateFence(new FenceCreateInfo
        {
            Flags = FenceCreateFlags.Signaled
        });

        var vertexSource = File.ReadAllBytes("shaders/v.spv");
        var vertexShader = device.CreateShaderModule(new ShaderModuleCreateInfo
        {
            CodeBytes = vertexSource,
            CodeSize = (nuint)vertexSource.Length,
        });

        var fragSource= File.ReadAllBytes("shaders/f.spv");
        var fragShader = device.CreateShaderModule(new ShaderModuleCreateInfo
        {
            CodeBytes = fragSource,
            CodeSize = (nuint)fragSource.Length,
        });

        var shaders = new PipelineShaderStageCreateInfo[2];
        shaders[0] = new PipelineShaderStageCreateInfo
        {
            Name = "main",
            Stage = ShaderStageFlags.Vertex,
            Module = vertexShader
        };
        shaders[1] = new PipelineShaderStageCreateInfo
        {
            Name = "main",
            Stage = ShaderStageFlags.Fragment,
            Module = fragShader
        };

        var layout = device.CreatePipelineLayout(new PipelineLayoutCreateInfo
        {
            PushConstantRangeCount = 0,
            SetLayoutCount = 0
        });

        var piplineInfo = new GraphicsPipelineCreateInfo
        {
            Subpass = 0,
            RenderPass = renderPass,
            DynamicState = new PipelineDynamicStateCreateInfo
            {
                DynamicStateCount = 2,
                DynamicStates = [DynamicState.Scissor, DynamicState.Viewport]
            },
            ColorBlendState = new PipelineColorBlendStateCreateInfo
            {
                AttachmentCount = 1,
                Attachments = [new PipelineColorBlendAttachmentState {
                    BlendEnable = false,
                    ColorWriteMask = ColorComponentFlags.R | ColorComponentFlags.G | ColorComponentFlags.B | ColorComponentFlags.A
                }],
                LogicOpEnable = false,
                LogicOp = LogicOp.Copy,
                BlendConstants = [0.0f, 0.0f, 0.0f, 0.0f],
            },
            MultisampleState = new PipelineMultisampleStateCreateInfo
            {
                RasterizationSamples = SampleCountFlags.Count1,
                SampleShadingEnable = false
            },
            RasterizationState = new PipelineRasterizationStateCreateInfo
            {
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1.0f,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.Clockwise,
                DepthBiasEnable = false,
            },
            ViewportState = new PipelineViewportStateCreateInfo
            {
                ViewportCount = 1,
                ScissorCount = 1
            },
            InputAssemblyState = new PipelineInputAssemblyStateCreateInfo
            {
                Topology = PrimitiveTopology.TriangleList,
                PrimitiveRestartEnable = false,
            },
            VertexInputState = new PipelineVertexInputStateCreateInfo
            {
                VertexAttributeDescriptionCount = 0,
                VertexBindingDescriptionCount = 0
            },
            StageCount = (uint)shaders.Length,
            Stages = shaders,
            Layout = layout
        };
        var graphicalPipline = device.CreateGraphicsPipelines(null, [piplineInfo])[0];

        while (!nativeWindow.IsClosing)
        {
            Glfw.PollEvents();

            device.WaitForFence(fence, true, ulong.MaxValue);
            device.ResetFence(fence);

            var imageIndex = device.AcquireNextImageKHR(swapchain, ulong.MaxValue, semophore, null);

            commandBuffer.Reset();
            commandBuffer.Begin(new CommandBufferBeginInfo());
            commandBuffer.CmdBeginRenderPass(new RenderPassBeginInfo
            {
                Framebuffer = frameBuffers[imageIndex],
                RenderPass = renderPass,
                RenderArea = new Rect2D
                {
                    Extent = surfaceCapabilities.CurrentExtent
                },
                ClearValueCount = 1,
                ClearValues = [new ClearValue { Color = new ClearColorValue([0.15f, 0.15f, 0.2f, 1.0f])}]
            }, SubpassContents.Inline);
            commandBuffer.CmdBindPipeline(PipelineBindPoint.Graphics, graphicalPipline);
            commandBuffer.CmdSetViewport(0, new Viewport
            {
                Width = surfaceCapabilities.CurrentExtent.Width,
                Height = surfaceCapabilities.CurrentExtent.Height,
                MaxDepth = 1.0f,
                MinDepth = 0.0f
            });
            commandBuffer.CmdSetScissor(0, new Rect2D
            {
                Extent = surfaceCapabilities.CurrentExtent,
            });
            commandBuffer.CmdDraw(3, 1, 0, 0);
            commandBuffer.CmdEndRenderPass();
            commandBuffer.End();

            queue.Submit(new SubmitInfo
            {
                CommandBufferCount = 1,
                CommandBuffers = [commandBuffer],
                SignalSemaphoreCount = 1,
                SignalSemaphores = [renderSemophore],
                WaitSemaphoreCount = 1,
                WaitSemaphores = [semophore],
                WaitDstStageMask = [PipelineStageFlags.AllCommands]
            }, fence);

            try
            {
                queue.PresentKHR(new PresentInfoKhr
                {
                    SwapchainCount = 1,
                    Swapchains = [swapchain],
                    WaitSemaphoreCount = 1,
                    WaitSemaphores = [renderSemophore],
                    ImageIndices = [imageIndex],
                });
            }
            catch (ResultException ex) when (ex.Result == Result.ErrorOutOfDateKhr)
            {

            }
        }
    }

    private Bool32 VulkanDebugCallback(DebugReportFlagsExt flags, DebugReportObjectTypeExt objectType, ulong objectHandle, IntPtr location, int messageCode, IntPtr layerPrefix, IntPtr message, IntPtr userData)
    {
        if (message != nint.Zero)
        {
            Console.WriteLine($"[VulkanAPI][{flags}][{messageCode}] {objectType} -> {Marshal.PtrToStringAnsi(message)}");
        }
        else
        {
            Console.WriteLine($"[VulkanAPI][{flags}][{messageCode}] {objectType} -> ----");
        }

        return true;
    }

    private static void LogPhysicalDevices(PhysicalDevice[] physicalDevices)
    {
        foreach (var physicalDevice in physicalDevices)
        {
            var properties = physicalDevice.GetProperties();
            Console.WriteLine($"Physical Device: [{properties.DeviceType}] {properties.DeviceName}");
        }
    }

    private SurfaceKhr CreateWindowSurface(Instance instance, nint windowPtr)
    {
        var result = GLFW.Vulkan.CreateWindowSurface(((IMarshalling)instance).Handle, windowPtr, nint.Zero, out var surfacePtr);
        if (result != nint.Zero)
        {
            throw ActivatorHelper.CreateInstance<ResultException>((Result)result);
        }

        var surface = ActivatorHelper.CreateInstance<SurfaceKhr>();
        ActivatorHelper.SetField<SurfaceKhr>(surface, "m", (ulong)surfacePtr);
        return surface;
    }

    private string[] GetRequierdInstanceExtensions()
    {
        var stringArrayPtr = VulkanInterop.GetRequiredInstanceExtensions(out var count);
        if (stringArrayPtr == nint.Zero)
        {
            return [];
        }

        var stringArray = new nint[count];
        Marshal.Copy(stringArrayPtr, stringArray, 0, (int)count);
        var extensions = new string[count];
        for (int i = 0; i < count; i++)
        {
            extensions[i] = Marshal.PtrToStringAnsi(stringArray[i])!;
        }

        return extensions;
    }

    static void Main(string[] args) => new VulkanApp().Run();
}
