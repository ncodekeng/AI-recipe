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
- disable parallel tool calls while using strict structured output;
- request `web_search_call.action.sources` in `include`;
- use a strict structured-output schema under `text.format`;
- bound tool calls, output tokens, ingredient count, and requested candidate count;
- send dietary restrictions from trusted backend fields and identify pantry text as untrusted data.

The response parser must collect HTTPS URLs from both `web_search_call.action.sources` and `output_text.annotations`. Normalize only fragments and a trailing slash. Accept a structured recipe only when its `sourceUrl` matches one of those collected URLs. Do not silently relax the comparison, follow a model-generated redirect, scrape the page, or create a replacement recipe.

The cited page is the canonical recipe. The model may extract its title, ingredient amounts, time, servings, cuisine, and a concise description. It must not reproduce or create the cooking method. The UI links to the publisher for instructions. A rough wine pairing is non-canonical assistance and must be blank for halal-style requests.

Azure web search does not expose a dependable licensed recipe-photo field. Leave the image URL empty unless a future documented provider contract supplies an allowed image; the client will use its deterministic fallback artwork.

Grounding with Bing has separate tool-call costs and can send request data outside the Azure compliance and geographic boundary. Keep privacy copy accurate and review Microsoft terms before production use.

Run the deterministic safety validator after mapping and before ranking. A missing citation, unsafe recipe, malformed response, limit response, or provider outage is a visible failure—not permission to fabricate a fallback.

Official references:

- <https://learn.microsoft.com/azure/ai-foundry/openai/how-to/web-search>
- <https://learn.microsoft.com/azure/ai-foundry/openai/how-to/structured-outputs>
- <https://learn.microsoft.com/azure/ai-foundry/openai/how-to/responses>
