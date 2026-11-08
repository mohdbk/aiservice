from moet_careers.portal import base_context

no_cache = 1


def get_context(context):
    base_context(context, "How we use AI and your data", "كيف نستخدم الذكاء الاصطناعي وبياناتك")
    return context
