/* Sample Flex Messages for the "Load sample" menu. */
(function () {
  "use strict";

  const receipt = {
    type: "bubble",
    body: {
      type: "box",
      layout: "vertical",
      contents: [
        { type: "text", text: "RECEIPT", weight: "bold", color: "#1DB446", size: "sm" },
        { type: "text", text: "Brown Store", weight: "bold", size: "xxl", margin: "md" },
        {
          type: "text",
          text: "Miraina Tower, 4-1-6 Shinjuku, Tokyo",
          size: "xs",
          color: "#aaaaaa",
          wrap: true,
        },
        { type: "separator", margin: "xxl" },
        {
          type: "box",
          layout: "vertical",
          margin: "xxl",
          spacing: "sm",
          contents: [
            {
              type: "box",
              layout: "horizontal",
              contents: [
                { type: "text", text: "Energy Drink", size: "sm", color: "#555555", flex: 0 },
                { type: "text", text: "$2.99", size: "sm", color: "#111111", align: "end" },
              ],
            },
            {
              type: "box",
              layout: "horizontal",
              contents: [
                { type: "text", text: "Chewing Gum", size: "sm", color: "#555555", flex: 0 },
                { type: "text", text: "$0.99", size: "sm", color: "#111111", align: "end" },
              ],
            },
            { type: "separator", margin: "xxl" },
            {
              type: "box",
              layout: "horizontal",
              margin: "xxl",
              contents: [
                { type: "text", text: "TOTAL", size: "sm", color: "#555555" },
                { type: "text", text: "$3.98", size: "sm", color: "#111111", align: "end" },
              ],
            },
          ],
        },
      ],
    },
  };

  const eyecatch = {
    type: "bubble",
    hero: {
      type: "image",
      url: "https://developers-resource.landpress.line.me/fx/img/01_1_cafe.png",
      size: "full",
      aspectRatio: "20:13",
      aspectMode: "cover",
    },
    body: {
      type: "box",
      layout: "vertical",
      contents: [
        { type: "text", text: "Brown Cafe", weight: "bold", size: "xl" },
        {
          type: "box",
          layout: "baseline",
          margin: "md",
          contents: [
            { type: "icon", size: "sm", url: "https://developers-resource.landpress.line.me/fx/img/review_gold_star_28.png" },
            { type: "icon", size: "sm", url: "https://developers-resource.landpress.line.me/fx/img/review_gold_star_28.png" },
            { type: "icon", size: "sm", url: "https://developers-resource.landpress.line.me/fx/img/review_gold_star_28.png" },
            { type: "icon", size: "sm", url: "https://developers-resource.landpress.line.me/fx/img/review_gold_star_28.png" },
            { type: "icon", size: "sm", url: "https://developers-resource.landpress.line.me/fx/img/review_gray_star_28.png" },
            { type: "text", text: "4.0", size: "sm", color: "#999999", margin: "md", flex: 0 },
          ],
        },
        {
          type: "box",
          layout: "vertical",
          margin: "lg",
          spacing: "sm",
          contents: [
            {
              type: "box",
              layout: "baseline",
              spacing: "sm",
              contents: [
                { type: "text", text: "Place", color: "#aaaaaa", size: "sm", flex: 1 },
                { type: "text", text: "Shinjuku, Tokyo", wrap: true, color: "#666666", size: "sm", flex: 5 },
              ],
            },
            {
              type: "box",
              layout: "baseline",
              spacing: "sm",
              contents: [
                { type: "text", text: "Time", color: "#aaaaaa", size: "sm", flex: 1 },
                { type: "text", text: "10:00 - 23:00", wrap: true, color: "#666666", size: "sm", flex: 5 },
              ],
            },
          ],
        },
      ],
    },
    footer: {
      type: "box",
      layout: "vertical",
      spacing: "sm",
      contents: [
        {
          type: "button",
          style: "link",
          height: "sm",
          action: { type: "uri", label: "CALL", uri: "https://example.com" },
        },
        {
          type: "button",
          style: "primary",
          height: "sm",
          color: "#905c44",
          action: { type: "uri", label: "WEBSITE", uri: "https://example.com" },
        },
      ],
    },
  };

  const carousel = {
    type: "carousel",
    contents: [
      {
        type: "bubble",
        size: "micro",
        hero: {
          type: "image",
          url: "https://developers-resource.landpress.line.me/fx/img/01_5_carousel.png",
          size: "full",
          aspectMode: "cover",
          aspectRatio: "1.51:1",
        },
        body: {
          type: "box",
          layout: "vertical",
          contents: [
            { type: "text", text: "Arm Chair, White", weight: "bold", size: "md", wrap: true },
            {
              type: "box",
              layout: "baseline",
              contents: [{ type: "text", text: "$49", wrap: true, weight: "bold", size: "xl", flex: 0 }],
            },
          ],
          spacing: "sm",
          paddingAll: "13px",
        },
      },
      {
        type: "bubble",
        size: "micro",
        hero: {
          type: "image",
          url: "https://developers-resource.landpress.line.me/fx/img/01_6_carousel.png",
          size: "full",
          aspectMode: "cover",
          aspectRatio: "1.51:1",
        },
        body: {
          type: "box",
          layout: "vertical",
          contents: [
            { type: "text", text: "Metal Desk Lamp", weight: "bold", size: "md", wrap: true },
            {
              type: "box",
              layout: "baseline",
              contents: [{ type: "text", text: "$11", wrap: true, weight: "bold", size: "xl", flex: 0 }],
            },
          ],
          spacing: "sm",
          paddingAll: "13px",
        },
      },
    ],
  };

  const minimal = {
    type: "bubble",
    body: {
      type: "box",
      layout: "vertical",
      contents: [{ type: "text", text: "Hello, World!", wrap: true }],
    },
  };

  window.FLEX_SAMPLES = [
    { id: "receipt", label: "Receipt", value: receipt },
    { id: "eyecatch", label: "Store card", value: eyecatch },
    { id: "carousel", label: "Carousel", value: carousel },
    { id: "minimal", label: "Minimal", value: minimal },
  ];
})();
