/*
 * LINE Flex Message renderer (browser global: window.FlexRenderer).
 *
 * Converts a Flex Message JSON tree (message wrapper, bubble, or carousel)
 * into DOM nodes styled with CSS flexbox to approximate LINE's rendering.
 * Size keyword -> px mappings follow LINE's documented scale (approximate).
 */
(function () {
  "use strict";

  // --- Keyword -> px maps -------------------------------------------------

  // spacing / margin / padding / cornerRadius / offset keywords
  const SPACE_PX = { none: 0, xs: 2, sm: 4, md: 8, lg: 12, xl: 16, xxl: 20 };

  // text / span / icon font size keywords
  const FONT_PX = {
    xxs: 11, xs: 13, sm: 14, md: 16, lg: 19, xl: 22, xxl: 29,
    "3xl": 35, "4xl": 48, "5xl": 74,
  };

  // image size keywords
  const IMAGE_PX = {
    xxs: 40, xs: 60, sm: 80, md: 100, lg: 120, xl: 140, xxl: 160,
    "3xl": 180, "4xl": 200, "5xl": 240,
  };

  // bubble width by size keyword
  const BUBBLE_W = {
    nano: 120, micro: 160, deca: 220, hecto: 241,
    kilo: 260, mega: 300, giga: 386,
  };

  // button heights
  const BUTTON_H = { sm: 40, md: 52 };

  const DEFAULT_LINK_COLOR = "#0367D3";
  const DEFAULT_PRIMARY_BG = "#17c950";
  const DEFAULT_SECONDARY_BG = "#dcdfe5";
  const DEFAULT_SEPARATOR_COLOR = "rgba(0,0,0,0.12)";
  const DEFAULT_BLOCK_PADDING = 20; // header/body/footer default paddingAll

  // --- helpers ------------------------------------------------------------

  function el(tag, className) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    return node;
  }

  function isObj(v) {
    return v && typeof v === "object" && !Array.isArray(v);
  }

  // Resolve a size-ish value (keyword | "10px" | "50%") into a CSS length.
  function lenFromKeyword(value, map) {
    if (value === undefined || value === null) return undefined;
    if (typeof value === "number") return value + "px";
    const s = String(value).trim();
    if (s in map) return map[s] + "px";
    if (/^\d+(\.\d+)?px$/.test(s)) return s;
    if (/^\d+(\.\d+)?%$/.test(s)) return s;
    if (/^\d+(\.\d+)?$/.test(s)) return s + "px"; // bare number string
    return s; // pass-through (e.g. "full" handled by caller)
  }

  function spacePx(value) {
    return lenFromKeyword(value, SPACE_PX);
  }

  // --- Text / Span --------------------------------------------------------

  function applyTextStyle(node, comp) {
    if (comp.size) {
      const px = FONT_PX[comp.size];
      node.style.fontSize = px ? px + "px" : lenFromKeyword(comp.size, FONT_PX);
    }
    if (comp.weight) node.style.fontWeight = comp.weight === "bold" ? "700" : "400";
    if (comp.color) node.style.color = comp.color;
    if (comp.style) node.style.fontStyle = comp.style; // normal | italic
    if (comp.decoration && comp.decoration !== "none") {
      node.style.textDecoration =
        comp.decoration === "line-through" ? "line-through" : comp.decoration;
    }
  }

  function renderSpan(span) {
    const node = el("span", "flex-span");
    node.textContent = span.text != null ? String(span.text) : "";
    applyTextStyle(node, span);
    return node;
  }

  function renderText(comp) {
    const node = el("div", "flex-text");
    const wrap = comp.wrap === true;
    node.style.whiteSpace = wrap ? "pre-wrap" : "nowrap";
    if (!wrap) {
      node.style.overflow = "hidden";
      node.style.textOverflow = "ellipsis";
    }
    if (comp.align) node.style.textAlign = comp.align; // start | end | center
    if (comp.lineSpacing) {
      const px = spacePx(comp.lineSpacing);
      if (px) node.style.lineHeight = `calc(1.15em + ${px})`;
    }
    if (wrap && comp.maxLines && comp.maxLines > 0) {
      node.style.display = "-webkit-box";
      node.style.webkitBoxOrient = "vertical";
      node.style.webkitLineClamp = String(comp.maxLines);
      node.style.overflow = "hidden";
    }
    applyTextStyle(node, comp);

    if (Array.isArray(comp.contents) && comp.contents.length) {
      for (const span of comp.contents) {
        if (isObj(span) && span.type === "span") node.appendChild(renderSpan(span));
      }
    } else {
      node.textContent = comp.text != null ? String(comp.text) : "";
    }
    return node;
  }

  // --- Image --------------------------------------------------------------

  function renderImage(comp) {
    const box = el("div", "flex-image");
    const size = comp.size || "md";

    if (size === "full") {
      box.style.width = "100%";
    } else if (IMAGE_PX[size]) {
      box.style.width = IMAGE_PX[size] + "px";
    } else {
      const l = lenFromKeyword(size, IMAGE_PX);
      if (l) box.style.width = l;
    }

    const ratio = parseAspectRatio(comp.aspectRatio) || 1;
    box.style.aspectRatio = String(ratio);

    if (comp.backgroundColor) box.style.background = comp.backgroundColor;
    if (comp.align) box.style.setProperty("--img-justify", alignToJustify(comp.align));

    const img = el("img");
    img.alt = "";
    img.src = comp.url || "";
    img.style.objectFit = comp.aspectMode === "cover" ? "cover" : "contain";
    img.addEventListener("error", function () {
      box.classList.add("flex-image--error");
      img.remove();
      const ph = el("div", "flex-image-placeholder");
      ph.textContent = "🖼 image";
      box.appendChild(ph);
    });
    box.appendChild(img);
    return box;
  }

  function parseAspectRatio(ar) {
    if (!ar) return undefined;
    const m = String(ar).split(":");
    if (m.length === 2) {
      const w = parseFloat(m[0]);
      const h = parseFloat(m[1]);
      if (w > 0 && h > 0) return w / h;
    }
    return undefined;
  }

  function alignToJustify(align) {
    if (align === "start") return "flex-start";
    if (align === "end") return "flex-end";
    return "center";
  }

  // --- Icon ---------------------------------------------------------------

  function renderIcon(comp) {
    const img = el("img", "flex-icon");
    img.alt = "";
    img.src = comp.url || "";
    const size = comp.size || "md";
    const px = FONT_PX[size] ? FONT_PX[size] : parseFloat(lenFromKeyword(size, FONT_PX)) || 16;
    img.style.height = px + "px";
    const ratio = parseAspectRatio(comp.aspectRatio) || 1;
    img.style.width = px * ratio + "px";
    img.addEventListener("error", function () {
      img.classList.add("flex-icon--error");
    });
    return img;
  }

  // --- Button -------------------------------------------------------------

  function renderButton(comp) {
    const wrap = el("div", "flex-button-wrap");
    const btn = el("div", "flex-button");
    const style = comp.style || "link";
    btn.classList.add("flex-button--" + style);

    const height = BUTTON_H[comp.height] || BUTTON_H.md;
    btn.style.height = height + "px";

    if (style === "primary" || style === "secondary") {
      btn.style.background =
        comp.color || (style === "primary" ? DEFAULT_PRIMARY_BG : DEFAULT_SECONDARY_BG);
      btn.style.color = style === "primary" ? "#fff" : "#111";
    } else {
      btn.style.color = comp.color || DEFAULT_LINK_COLOR;
    }

    const label = (comp.action && comp.action.label) || "";
    btn.textContent = label;
    if (comp.gravity) wrap.style.alignSelf = gravityToAlign(comp.gravity);
    wrap.appendChild(btn);
    return wrap;
  }

  function gravityToAlign(g) {
    if (g === "top") return "flex-start";
    if (g === "bottom") return "flex-end";
    if (g === "center") return "center";
    return undefined;
  }

  // --- Separator ----------------------------------------------------------

  function renderSeparator(comp, parentLayout) {
    const sep = el("div", "flex-separator");
    const color = comp.color || DEFAULT_SEPARATOR_COLOR;
    if (parentLayout === "horizontal") {
      sep.style.width = "1px";
      sep.style.alignSelf = "stretch";
      sep.style.background = color;
    } else {
      sep.style.height = "1px";
      sep.style.background = color;
    }
    return sep;
  }

  // --- Video (preview only) ----------------------------------------------

  function renderVideo(comp) {
    const box = el("div", "flex-image flex-video");
    const ratio = parseAspectRatio(comp.aspectRatio) || 16 / 9;
    box.style.aspectRatio = String(ratio);
    box.style.width = "100%";
    if (comp.previewUrl) {
      const img = el("img");
      img.src = comp.previewUrl;
      img.style.objectFit = "cover";
      img.addEventListener("error", () => img.remove());
      box.appendChild(img);
    }
    const play = el("div", "flex-video-play");
    play.textContent = "▶";
    box.appendChild(play);
    return box;
  }

  // --- Box ----------------------------------------------------------------

  function renderBox(comp, opts) {
    opts = opts || {};
    const layout = comp.layout || "vertical";
    const node = el("div", "flex-box flex-box--" + layout);

    if (layout === "horizontal") node.style.flexDirection = "row";
    else if (layout === "baseline") {
      node.style.flexDirection = "row";
      node.style.alignItems = "baseline";
    } else node.style.flexDirection = "column";

    // padding
    const padProps = ["paddingAll", "paddingTop", "paddingBottom", "paddingStart", "paddingEnd"];
    const hasPad = padProps.some((p) => comp[p] !== undefined);
    if (comp.paddingAll !== undefined) node.style.padding = spacePx(comp.paddingAll);
    if (comp.paddingTop !== undefined) node.style.paddingTop = spacePx(comp.paddingTop);
    if (comp.paddingBottom !== undefined) node.style.paddingBottom = spacePx(comp.paddingBottom);
    if (comp.paddingStart !== undefined) node.style.paddingInlineStart = spacePx(comp.paddingStart);
    if (comp.paddingEnd !== undefined) node.style.paddingInlineEnd = spacePx(comp.paddingEnd);
    if (!hasPad && opts.blockPadding !== undefined) {
      node.style.padding = opts.blockPadding + "px";
    }

    if (comp.backgroundColor) node.style.background = comp.backgroundColor;
    if (comp.borderColor) node.style.borderColor = comp.borderColor;
    if (comp.borderWidth !== undefined) {
      node.style.borderStyle = "solid";
      node.style.borderWidth = lenFromKeyword(comp.borderWidth, SPACE_PX);
    }
    if (comp.cornerRadius !== undefined) node.style.borderRadius = spacePx(comp.cornerRadius);
    if (comp.width !== undefined) node.style.width = lenFromKeyword(comp.width, {});
    if (comp.height !== undefined) node.style.height = lenFromKeyword(comp.height, {});
    if (comp.justifyContent) node.style.justifyContent = comp.justifyContent;
    if (comp.alignItems) node.style.alignItems = comp.alignItems;

    if (comp.position === "absolute") {
      node.style.position = "absolute";
      if (comp.offsetTop !== undefined) node.style.top = spacePx(comp.offsetTop);
      if (comp.offsetBottom !== undefined) node.style.bottom = spacePx(comp.offsetBottom);
      if (comp.offsetStart !== undefined) node.style.insetInlineStart = spacePx(comp.offsetStart);
      if (comp.offsetEnd !== undefined) node.style.insetInlineEnd = spacePx(comp.offsetEnd);
    } else if (comp.position === "relative") {
      node.style.position = "relative";
      if (comp.offsetTop !== undefined) node.style.top = spacePx(comp.offsetTop);
      if (comp.offsetStart !== undefined) node.style.insetInlineStart = spacePx(comp.offsetStart);
    }

    const spacing = comp.spacing;
    const children = Array.isArray(comp.contents) ? comp.contents : [];
    children.forEach((child, i) => {
      if (!isObj(child)) return;
      const childNode = renderComponent(child, layout);
      if (!childNode) return;
      applyChildLayout(childNode, child, layout, i, spacing);
      node.appendChild(childNode);
    });

    return node;
  }

  // Apply flex/margin of a child within its parent box.
  function applyChildLayout(node, child, parentLayout, index, parentSpacing) {
    const horizontal = parentLayout === "horizontal" || parentLayout === "baseline";

    // leading space: explicit margin, else parent spacing (skip first child)
    let lead;
    if (child.margin !== undefined) lead = spacePx(child.margin);
    else if (index > 0 && parentSpacing !== undefined) lead = spacePx(parentSpacing);
    if (lead && lead !== "0px") {
      if (horizontal) node.style.marginInlineStart = lead;
      else node.style.marginTop = lead;
    }

    // flex sizing
    let flex = child.flex;
    if (flex === undefined) flex = horizontal ? 1 : 0;
    if (child.width !== undefined && parentLayout !== "horizontal") {
      // fixed width in vertical box: don't grow
      node.style.flex = "0 0 auto";
    } else if (flex > 0) {
      node.style.flex = flex + " 1 0%";
      node.style.minWidth = "0";
    } else {
      node.style.flex = "0 0 auto";
    }

    // gravity (cross-axis in horizontal/baseline)
    if (horizontal && child.gravity) {
      const a = gravityToAlign(child.gravity);
      if (a) node.style.alignSelf = a;
    }
  }

  // --- Component dispatch -------------------------------------------------

  function renderComponent(comp, parentLayout) {
    try {
      switch (comp.type) {
        case "box":
          return renderBox(comp);
        case "text":
          return renderText(comp);
        case "image":
          return renderImage(comp);
        case "button":
          return renderButton(comp);
        case "icon":
          return renderIcon(comp);
        case "separator":
          return renderSeparator(comp, parentLayout);
        case "video":
          return renderVideo(comp);
        case "filler": {
          const f = el("div", "flex-filler");
          return f;
        }
        case "span": {
          // span outside text: render inline text
          return renderText({ type: "text", contents: [comp] });
        }
        default:
          return unknown(comp.type);
      }
    } catch (e) {
      return unknown((comp && comp.type) + " (error)");
    }
  }

  function unknown(type) {
    const node = el("div", "flex-unknown");
    node.textContent = "⚠ unknown: " + (type || "?");
    return node;
  }

  // --- Bubble / Carousel --------------------------------------------------

  function renderBubble(bubble) {
    const size = bubble.size || "mega";
    const width = BUBBLE_W[size] || BUBBLE_W.mega;
    const node = el("div", "flex-bubble");
    node.style.width = width + "px";
    if (bubble.direction === "rtl") node.style.direction = "rtl";

    const styles = isObj(bubble.styles) ? bubble.styles : {};
    const blocks = ["header", "hero", "body", "footer"];
    let prevBlockPainted = false;

    blocks.forEach((name) => {
      const block = bubble[name];
      if (!block) return;
      const blockStyle = isObj(styles[name]) ? styles[name] : {};

      // separator between this block and the previous one
      if (prevBlockPainted && blockStyle.separator) {
        const sep = el("div", "flex-block-separator");
        sep.style.background = blockStyle.separatorColor || DEFAULT_SEPARATOR_COLOR;
        node.appendChild(sep);
      }

      let blockNode;
      if (name === "hero") {
        // hero is usually an image or a box; no default padding
        blockNode =
          block.type === "box"
            ? renderBox(block, { blockPadding: undefined })
            : renderComponent(block, "vertical");
        if (blockNode) blockNode.classList.add("flex-block", "flex-hero");
      } else {
        blockNode = renderBox(block, { blockPadding: DEFAULT_BLOCK_PADDING });
        blockNode.classList.add("flex-block", "flex-" + name);
      }
      if (!blockNode) return;
      if (blockStyle.backgroundColor) blockNode.style.background = blockStyle.backgroundColor;
      node.appendChild(blockNode);
      prevBlockPainted = true;
    });

    return node;
  }

  function renderCarousel(carousel) {
    const node = el("div", "flex-carousel");
    const contents = Array.isArray(carousel.contents) ? carousel.contents : [];
    contents.forEach((bubble) => {
      if (isObj(bubble) && bubble.type === "bubble") {
        const b = renderBubble(bubble);
        b.classList.add("flex-carousel-item");
        node.appendChild(b);
      }
    });
    return node;
  }

  // Normalize any accepted shape into a container object {type:bubble|carousel}
  function extractContainer(json) {
    if (!isObj(json)) return null;
    if (json.type === "flex" && isObj(json.contents)) return json.contents;
    if (json.type === "bubble" || json.type === "carousel") return json;
    if (isObj(json.contents) && (json.contents.type === "bubble" || json.contents.type === "carousel")) {
      return json.contents;
    }
    return null;
  }

  // Lightweight structural validation shared by every controller.
  function validate(json) {
    const w = [];
    const container = extractContainer(json);
    if (!container) {
      w.push("No bubble/carousel container at the root");
      return w;
    }
    let bubbles = [];
    if (container.type === "carousel") {
      if (!Array.isArray(container.contents) || container.contents.length === 0) {
        w.push("carousel contents is empty");
      } else {
        if (container.contents.length > 12) w.push("A carousel supports at most 12 bubbles");
        bubbles = container.contents;
      }
    } else {
      bubbles = [container];
    }
    bubbles.forEach((b, i) => {
      if (!b || b.type !== "bubble") {
        w.push(`contents[${i}] is not a bubble`);
        return;
      }
      if (!b.header && !b.hero && !b.body && !b.footer) {
        w.push(`bubble[${i}] has no header/hero/body/footer`);
      }
    });
    return w;
  }

  // --- Public API ---------------------------------------------------------

  function render(target, json) {
    target.innerHTML = "";
    const container = extractContainer(json);
    if (!container) {
      const err = el("div", "flex-render-error");
      err.textContent =
        'The root must be a "bubble" / "carousel" container, or a type:"flex" message.';
      target.appendChild(err);
      return;
    }
    let node;
    if (container.type === "carousel") node = renderCarousel(container);
    else node = renderBubble(container);
    target.appendChild(node);
  }

  window.FlexRenderer = { render: render, extractContainer: extractContainer, validate: validate };
})();
