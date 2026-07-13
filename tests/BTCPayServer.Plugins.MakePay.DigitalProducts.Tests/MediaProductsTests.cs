#nullable enable
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Models;
using BTCPayServer.Plugins.MakePay.DigitalProducts.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace BTCPayServer.Plugins.MakePay.DigitalProducts.Tests;

public sealed class MediaProductsTests
{
    [Fact]
    public void Category_rule_values_remain_backward_compatible()
    {
        Assert.Equal(0, (int)DigitalProductType.FileDownload);
        Assert.Equal(1, (int)DigitalProductType.PdfEbook);
        Assert.Equal(2, (int)DigitalProductType.Audio);
        Assert.Equal(3, (int)DigitalProductType.Video);
        Assert.Equal(4, (int)DigitalProductType.PhotosArt);
        Assert.Equal(0, (int)DigitalDeliveryMode.Download);
        Assert.Equal(1, (int)DigitalDeliveryMode.Stream);
        Assert.Equal(2, (int)DigitalDeliveryMode.StreamAndDownload);
        Assert.Equal(0, (int)DigitalStoreCategoryRule.Custom);
        Assert.Equal(1, (int)DigitalStoreCategoryRule.Downloads);
        Assert.Equal(2, (int)DigitalStoreCategoryRule.Licenses);
        Assert.Equal(3, (int)DigitalStoreCategoryRule.PdfEbooks);
        Assert.Equal(4, (int)DigitalStoreCategoryRule.Audio);
        Assert.Equal(5, (int)DigitalStoreCategoryRule.Video);
        Assert.Equal(6, (int)DigitalStoreCategoryRule.PhotosArt);
    }

    [Fact]
    public void Default_categories_cover_every_supported_product_family()
    {
        var categories = DigitalDownloadsSettings.DefaultCategories();

        Assert.Equal(6, categories.Count);
        Assert.Equal(
            [
                DigitalStoreCategoryRule.Downloads,
                DigitalStoreCategoryRule.PdfEbooks,
                DigitalStoreCategoryRule.Audio,
                DigitalStoreCategoryRule.Video,
                DigitalStoreCategoryRule.PhotosArt,
                DigitalStoreCategoryRule.Licenses
            ],
            categories.Select(category => category.Rule));
        Assert.Equal(categories.Count, categories.Select(category => category.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Catalog_subtitle_is_optional_for_live_editor_saves()
    {
        var property = typeof(DigitalDownloadsSettings).GetProperty(nameof(DigitalDownloadsSettings.CatalogSubtitle))!;
        var nullability = new NullabilityInfoContext().Create(property);

        Assert.Equal(NullabilityState.Nullable, nullability.WriteState);
    }

    [Fact]
    public void Media_categories_match_only_their_product_type_and_hide_when_empty()
    {
        var settings = new DigitalDownloadsSettings();
        var catalog = new[]
        {
            Product("file", DigitalProductType.FileDownload),
            Product("ebook", DigitalProductType.PdfEbook),
            Product("audio", DigitalProductType.Audio),
            Product("video", DigitalProductType.Video),
            Product("photos", DigitalProductType.PhotosArt)
        };

        var categories = DigitalStorefrontBuilder.BuildCategories(settings, catalog);

        Assert.Equal(5, categories.Count);
        Assert.DoesNotContain(categories, category => category.Slug == "licenses");
        Assert.Equal("file", ProductFor("downloads").Id);
        Assert.Equal("ebook", ProductFor("ebooks").Id);
        Assert.Equal("audio", ProductFor("audio").Id);
        Assert.Equal("video", ProductFor("video").Id);
        Assert.Equal("photos", ProductFor("photos-art").Id);

        StoreProductViewModel ProductFor(string slug)
        {
            var category = Assert.Single(categories, category => category.Slug == slug);
            return Assert.Single(DigitalStorefrontBuilder.FilterCatalog(catalog, category));
        }
    }

    [Fact]
    public void Legacy_download_without_a_media_type_stays_in_downloads_category()
    {
        var legacy = Product("legacy", null);
        var settings = new DigitalDownloadsSettings
        {
            StorefrontCategories =
            [
                new DigitalStoreCategory
                {
                    Id = "downloads",
                    Name = "Downloads",
                    Slug = "downloads",
                    Rule = DigitalStoreCategoryRule.Downloads
                }
            ]
        };

        var category = Assert.Single(DigitalStorefrontBuilder.BuildCategories(settings, [legacy]));
        Assert.Equal("legacy", Assert.Single(DigitalStorefrontBuilder.FilterCatalog([legacy], category)).Id);
    }

    [Theory]
    [InlineData(DigitalProductType.FileDownload, "File download", "download", "Preview sample")]
    [InlineData(DigitalProductType.PdfEbook, "PDF / ebook", "ebook", "Read preview")]
    [InlineData(DigitalProductType.Audio, "Music & audio", "audio", "Listen to demo")]
    [InlineData(DigitalProductType.Video, "Video content", "video", "Watch trailer")]
    [InlineData(DigitalProductType.PhotosArt, "Photos & art", "photo", "View gallery")]
    public void Media_type_labels_assets_and_preview_actions_are_consistent(
        DigitalProductType type,
        string typeLabel,
        string assetKind,
        string previewAction)
    {
        var product = Product("product", type);

        Assert.Equal(typeLabel, DigitalStorefrontBuilder.ProductTypeLabel(type));
        Assert.Equal(typeLabel, DigitalStorefrontBuilder.ProductTypeLabel(product));
        Assert.Equal(assetKind, DigitalStorefrontBuilder.ProductAssetKind(product));
        Assert.Equal(previewAction, DigitalStorefrontBuilder.PreviewActionLabel(type));
    }

    [Fact]
    public void Media_metadata_uses_type_specific_details_and_delivery_mode()
    {
        Assert.Equal("240 pages · Read online", DigitalStorefrontBuilder.ProductMeta(new DigitalProduct
        {
            ProductType = DigitalProductType.PdfEbook,
            DeliveryMode = DigitalDeliveryMode.Stream,
            PageCount = 240
        }));
        Assert.Equal("3:05 · Protected streaming", DigitalStorefrontBuilder.ProductMeta(new DigitalProduct
        {
            ProductType = DigitalProductType.Audio,
            DeliveryMode = DigitalDeliveryMode.Stream,
            DurationSeconds = 185
        }));
        Assert.Equal("1:02:03 · Stream or download", DigitalStorefrontBuilder.ProductMeta(new DigitalProduct
        {
            ProductType = DigitalProductType.Video,
            DeliveryMode = DigitalDeliveryMode.StreamAndDownload,
            DurationSeconds = 3723
        }));
        Assert.Equal("12 assets · Protected private download", DigitalStorefrontBuilder.ProductMeta(new DigitalProduct
        {
            ProductType = DigitalProductType.PhotosArt,
            DeliveryMode = DigitalDeliveryMode.Download,
            AssetCount = 12
        }));
        Assert.Equal($"1{CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator}5 MB · Protected private download", DigitalStorefrontBuilder.ProductMeta(new DigitalProduct
        {
            ProductType = DigitalProductType.FileDownload,
            FileSize = 1572864
        }));
    }

    [Fact]
    public void Product_snapshot_round_trips_fulfillment_critical_fields()
    {
        var source = new DigitalProduct
        {
            Id = "course-2026",
            Slug = "course-2026",
            Name = "Video course",
            ImageUrl = "https://example.com/cover.webp",
            ProductType = DigitalProductType.Video,
            DeliveryMode = DigitalDeliveryMode.StreamAndDownload,
            StorageKind = ProductStorageKind.S3,
            StorageLocation = "courses/2026/master.mp4",
            DownloadFileName = "video-course.mp4",
            ContentType = "video/mp4",
            FileSize = 987654321,
            DownloadLimit = 4,
            LinkHours = 120,
            Price = 49m
        };

        var snapshot = DigitalProductSnapshot.From(source);
        source.StorageLocation = "changed-after-checkout.mp4";
        source.DownloadLimit = 1;
        var restored = snapshot.ToProduct();

        Assert.Equal("course-2026", restored.Id);
        Assert.Equal("course2026", restored.Slug);
        Assert.Equal(DigitalProductType.Video, restored.ProductType);
        Assert.Equal(DigitalDeliveryMode.StreamAndDownload, restored.DeliveryMode);
        Assert.Equal(ProductStorageKind.S3, restored.StorageKind);
        Assert.Equal("courses/2026/master.mp4", restored.StorageLocation);
        Assert.Equal("video-course.mp4", restored.DownloadFileName);
        Assert.Equal("video/mp4", restored.ContentType);
        Assert.Equal(987654321, restored.FileSize);
        Assert.Equal(4, restored.DownloadLimit);
        Assert.Equal(120, restored.LinkHours);
    }

    [Fact]
    public void Legacy_snapshot_json_defaults_to_file_download_and_download_delivery()
    {
        const string json = """
            {
              "Id": "legacy-product",
              "Name": "Legacy file",
              "StorageKind": 0,
              "StorageLocation": "store/product/file.zip",
              "DownloadFileName": "file.zip",
              "ContentType": "application/zip"
            }
            """;

        var snapshot = JsonSerializer.Deserialize<DigitalProductSnapshot>(json)!;
        var product = snapshot.ToProduct();

        Assert.Equal(DigitalProductType.FileDownload, product.ProductType);
        Assert.Equal(DigitalDeliveryMode.Download, product.DeliveryMode);
        Assert.Equal("legacy-product", product.Id);
        Assert.Equal("file.zip", product.DownloadFileName);
    }

    [Fact]
    public void Checkout_keeps_an_immutable_media_delivery_snapshot()
    {
        var source = new DigitalProduct
        {
            Id = "audio",
            Slug = "audio",
            Name = "Album",
            Description = "Lossless recording",
            Price = 12m,
            Active = true,
            ProductType = DigitalProductType.Audio,
            DeliveryMode = DigitalDeliveryMode.StreamAndDownload,
            StorageKind = ProductStorageKind.Local,
            StorageLocation = "store/audio/master.flac",
            DownloadFileName = "album.flac",
            ContentType = "audio/flac"
        };
        var service = new DigitalCheckoutService();
        var catalog = service.BuildCatalog([source], []);
        var lines = service.ResolveCart(
            new DigitalCartState { Lines = [new DigitalCartLine { Kind = DigitalProductKind.Download, ProductId = source.Id }] },
            catalog);

        source.StorageLocation = "changed.flac";
        source.DeliveryMode = DigitalDeliveryMode.Download;
        var checkout = service.Create("store", "buyer@example.com", "usd", lines, "https://example.com");
        var snapshot = Assert.Single(checkout.Lines).DigitalProductSnapshot!;

        Assert.Equal("store/audio/master.flac", snapshot.StorageLocation);
        Assert.Equal(DigitalDeliveryMode.StreamAndDownload, snapshot.DeliveryMode);
        Assert.Equal(DigitalProductType.Audio, snapshot.ProductType);
    }

    [Theory]
    [MemberData(nameof(ValidPreviewFiles))]
    public async Task Preview_validation_accepts_supported_mime_and_matching_signature(
        DigitalProductType type,
        string contentType,
        byte[] contents)
    {
        using var fixture = new ProductFileFixture();
        var upload = Upload(contents, "preview.bin", contentType);

        Assert.Null(await fixture.Service.ValidatePreviewAsset(type, upload, CancellationToken.None));
    }

    [Fact]
    public async Task Preview_validation_rejects_wrong_signature_wrong_media_family_and_proxy_oversize()
    {
        using var fixture = new ProductFileFixture();

        var signatureError = await fixture.Service.ValidatePreviewAsset(
            DigitalProductType.PdfEbook,
            Upload("not a pdf"u8.ToArray(), "preview.pdf", "application/pdf"),
            CancellationToken.None);
        var mediaTypeError = await fixture.Service.ValidatePreviewAsset(
            DigitalProductType.Video,
            Upload("%PDF-1.7"u8.ToArray(), "preview.pdf", "application/pdf"),
            CancellationToken.None);
        var oversized = new FormFile(new MemoryStream([0x89]), 0, 96L * 1024 * 1024, "preview", "huge.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        var sizeError = await fixture.Service.ValidatePreviewAsset(DigitalProductType.PhotosArt, oversized, CancellationToken.None);

        Assert.Contains("contents do not match", signatureError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not valid for Video content", mediaTypeError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("95 MB or smaller", sizeError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Photo_pack_accepts_common_browser_zip_mime_alias()
    {
        using var fixture = new ProductFileFixture();
        var zip = Upload([0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0], "art-pack.zip", "application/x-zip-compressed");

        Assert.Null(await fixture.Service.ValidateProductAsset(DigitalProductType.PhotosArt, zip, CancellationToken.None));
    }

    [Fact]
    public void Segmented_range_download_can_finish_after_first_chunk_reaches_limit()
    {
        var now = DateTimeOffset.UtcNow;
        var range = new RangeHeaderValue(1024, 2047);
        var order = new DigitalOrder { MaxDownloads = 1, DownloadCount = 0 };

        Assert.True(DigitalDownloadAccessPolicy.CanStartOrContinue(order, range, now));
        order.DownloadCount = 1;
        order.LastDownloadAt = now;
        Assert.True(DigitalDownloadAccessPolicy.CanStartOrContinue(order, range, now.AddMinutes(1)));
        Assert.False(DigitalDownloadAccessPolicy.CanStartOrContinue(order, null, now.AddMinutes(1)));
        Assert.False(DigitalDownloadAccessPolicy.CanStartOrContinue(order, new RangeHeaderValue(0, 2047), now.AddMinutes(1)));
        Assert.False(DigitalDownloadAccessPolicy.CanStartOrContinue(order, range, now.AddMinutes(31)));
    }

    [Fact]
    public async Task Local_original_is_scoped_to_its_store_and_product()
    {
        using var fixture = new ProductFileFixture();
        var saved = await fixture.Service.SaveLocal(
            "store-a",
            "product-a",
            Upload("private original"u8.ToArray(), "original.bin", "application/octet-stream"),
            CancellationToken.None);
        var product = new DigitalProduct
        {
            Id = "product-a",
            StorageKind = ProductStorageKind.Local,
            StorageLocation = saved.RelativePath,
            ContentType = saved.ContentType,
            DownloadFileName = saved.FileName,
            Price = 1m
        };

        await using (var file = await fixture.Service.Open("store-a", product, new DigitalDownloadsSettings(), null, CancellationToken.None))
        using (var reader = new StreamReader(file.Stream, Encoding.UTF8, leaveOpen: true))
            Assert.Equal("private original", await reader.ReadToEndAsync());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.Open("store-b", product, new DigitalDownloadsSettings(), null, CancellationToken.None));
        product.Id = "product-b";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.Open("store-a", product, new DigitalDownloadsSettings(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Public_preview_is_scoped_to_store_product_and_preview_asset()
    {
        using var fixture = new ProductFileFixture();
        var upload = Upload("%PDF-1.7 preview"u8.ToArray(), "sample.pdf", "application/pdf");
        var asset = await fixture.Service.SavePreviewLocal("store-a", "ebook-a", "Sample chapter", upload, CancellationToken.None);

        Assert.Contains("storea/ebooka/previews/", asset.StorageLocation, StringComparison.Ordinal);
        Assert.EndsWith("sample.pdf", asset.FileName, StringComparison.Ordinal);
        await using (var file = fixture.Service.OpenPreview("store-a", "ebook-a", asset))
            Assert.Equal("application/pdf", file.ContentType);

        Assert.Throws<UnauthorizedAccessException>(() => fixture.Service.OpenPreview("store-b", "ebook-a", asset));
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Service.OpenPreview("store-a", "ebook-b", asset));
        var wrongAsset = Clone(asset);
        wrongAsset.Id = "another-preview";
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Service.OpenPreview("store-a", "ebook-a", wrongAsset));
        var remoteAsset = Clone(asset);
        remoteAsset.StorageKind = ProductStorageKind.CustomUrl;
        Assert.Throws<InvalidOperationException>(() => fixture.Service.OpenPreview("store-a", "ebook-a", remoteAsset));
    }

    [Fact]
    public async Task Remote_origin_cannot_override_the_validated_inline_media_type()
    {
        using var fixture = new ProductFileFixture(new StaticResponseHandler(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("<script>alert(1)</script>"u8.ToArray())
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return response;
        }));
        var product = new DigitalProduct
        {
            Id = "video-a",
            ProductType = DigitalProductType.Video,
            DeliveryMode = DigitalDeliveryMode.Stream,
            StorageKind = ProductStorageKind.CustomUrl,
            StorageLocation = "https://1.1.1.1/video",
            ContentType = "video/mp4",
            DownloadFileName = "video.mp4",
            Price = 1m
        };

        await using var remote = await fixture.Service.Open("store-a", product, new DigitalDownloadsSettings(), null, CancellationToken.None);

        Assert.Equal("video/mp4", remote.ContentType);
    }

    public static TheoryData<DigitalProductType, string, byte[]> ValidPreviewFiles => new()
    {
        { DigitalProductType.PdfEbook, "application/pdf", "%PDF-1.7\n"u8.ToArray() },
        { DigitalProductType.Audio, "audio/mpeg", "ID3\u0004\0\0"u8.ToArray() },
        { DigitalProductType.Audio, "audio/flac", "fLaC\0\0\0\0"u8.ToArray() },
        { DigitalProductType.Audio, "audio/wav", [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45] },
        { DigitalProductType.Audio, "audio/ogg", "OggS\0\0\0\0"u8.ToArray() },
        { DigitalProductType.Video, "video/mp4", [0, 0, 0, 20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D] },
        { DigitalProductType.Video, "video/webm", [0x1A, 0x45, 0xDF, 0xA3] },
        { DigitalProductType.PhotosArt, "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] },
        { DigitalProductType.PhotosArt, "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0] },
        { DigitalProductType.PhotosArt, "image/webp", [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50] }
    };

    private static StoreProductViewModel Product(string id, DigitalProductType? type) => new()
    {
        Kind = DigitalProductKind.Download,
        Id = id,
        Slug = id,
        Name = id,
        Description = "Description",
        Price = 1m,
        ProductType = type
    };

    private static FormFile Upload(byte[] contents, string fileName, string contentType)
    {
        var stream = new MemoryStream(contents, writable: false);
        return new FormFile(stream, 0, stream.Length, "preview", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static DigitalProductPreviewAsset Clone(DigitalProductPreviewAsset asset) => new()
    {
        Id = asset.Id,
        Label = asset.Label,
        AltText = asset.AltText,
        SortOrder = asset.SortOrder,
        StorageKind = asset.StorageKind,
        StorageLocation = asset.StorageLocation,
        FileName = asset.FileName,
        ContentType = asset.ContentType,
        FileSize = asset.FileSize,
        Width = asset.Width,
        Height = asset.Height,
        Watermarked = asset.Watermarked
    };

    private sealed class ProductFileFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "makepay-media-tests-" + Guid.NewGuid().ToString("N"));

        public ProductFileFixture(HttpMessageHandler? handler = null)
        {
            Directory.CreateDirectory(_directory);
            var directories = Options.Create(new DataDirectories { DataDir = _directory });
            var tokens = new DownloadTokenService(new EphemeralDataProtectionProvider());
            Service = new ProductFileService(handler is null ? new HttpClient() : new HttpClient(handler), directories, tokens);
        }

        public ProductFileService Service { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class StaticResponseHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory());
    }
}
