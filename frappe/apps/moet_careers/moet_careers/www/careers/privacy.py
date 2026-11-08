from moet_careers.portal import base_context

no_cache = 1


def get_context(context):
    base_context(context, "Privacy notice for applicants", "إشعار الخصوصية للمتقدمين")
    return context
