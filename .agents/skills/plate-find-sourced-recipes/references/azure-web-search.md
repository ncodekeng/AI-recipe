# Azure web-grounded recipe contract

Use Azure OpenAI's v1 Responses endpoint:

```text
POST {endpoint}/openai/v1/responses
```

The request must:

- use the configured deployment in `model`;
- set `store` to `false`;
- include `{ "type": "web_search" }` in `tools`;
- pass the configured two-letter country as an approximate `user_location` on that tool;
- set `tool_choice` to `required`;
- disable parallel tool calls;
- request `web_search_call.action.sources` in `include`;
- leave the response format in its default text mode because Azure deployments can reject JSON mode when `web_search` is present;
- instruct the model to return one JSON object with every required recipe and ingredient field;
- bound tool calls, output tokens, ingredient count, and requested candidate count;
- ask for multiple distinct publisher recipes, enough candidates to display up to six validated results, and make at most the configured number of additional searches when too few valid distinct results are returned;
- send dietary restrictions from trusted backend fields and identify pantry text as untrusted data.

The response parser must collect HTTPS URLs from both `web_search_call.action.sources` and `output_text.annotations`. Normalize only fragments and a trailing slash. Accept a structured recipe only when its `sourceUrl` matches one of those collected URLs. It may extract a JSON object from an otherwise wrapped output. If Azure returns prose rather than JSON, retry once in default text mode with a stronger JSON-only instruction and a fresh required web search, then validate only against that retry's returned sources. Never send `text.format` as `json_schema` or `json_object` on this path. If the retry is still not readable JSON, fail clearly. Do not silently relax the URL comparison, follow a model-generated redirect, scrape the page, or create a replacement recipe.

The cited page is the canonical recipe. The model may extract its title, ingredient amounts, time, servings, and cuisine. It must not reproduce or claim to provide the publisher's cooking method. When no licensed method is available, it may create a separate concise cooking guide for the same dish using only the listed recipe ingredients. The API must mark that guide `AiGenerated`, and the UI must visibly say that it is not publisher instructions and link to the publisher for the canonical method. A rough wine pairing is non-canonical assistance and must be blank for halal-style requests.

Azure web search does not expose a dependable licensed recipe-photo field. Do not ask it for an image URL or trust a model-generated license claim. After recipe ranking, the separate commercial-image client may search Wikimedia Commons and read the file's structured image-info metadata. It must allow only CC0, Public Domain, CC BY, or CC BY-SA bitmap files; require complete attribution metadata for attribution licenses; and clear every image field on any uncertainty. The response must keep the image URL, Commons file-description URL, license type, license URL, and attribution requirements together. The client uses deterministic fallback artwork when no image passes. For local visual testing only, an explicit flag plus the Development host environment may allow a relevant Commons bitmap without verified rights; return `UnverifiedTestOnly`, show the warning in the UI, and make production ignore this fallback.

Grounding with Bing has separate tool-call costs and can send request data outside the Azure compliance and geographic boundary. Keep privacy copy accurate and review Microsoft terms before production use.

Run the deterministic safety validator after mapping and before ranking. A missing citation, unsafe recipe, malformed response, limit response, or provider outage is a visible failure—not permission to fabricate a fallback.

Official references:

- <https://learn.microsoft.com/azure/ai-foundry/openai/how-to/web-search>
- <https://learn.microsoft.com/azure/ai-foundry/openai/how-to/structured-outputs>
- <https://learn.microsoft.com/azure/ai-foundry/openai/how-to/responses>
