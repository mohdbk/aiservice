frappe.ui.form.on("Job Applicant", {
	refresh(frm) {
		if (frm.is_new()) return;
		frm.add_custom_button(__("Invite to Assessment"), () => {
			const d = new frappe.ui.Dialog({
				title: __("Invite to Assessment"),
				fields: [
					{ fieldname: "assessment", fieldtype: "Link", options: "Assessment", label: __("Assessment"), reqd: 1,
					  get_query: () => ({ filters: { status: "Published" } }) },
					{ fieldname: "deadline_days", fieldtype: "Int", label: __("Deadline (Days)"), default: 7 },
				],
				primary_action_label: __("Send Invitation"),
				primary_action(values) {
					frappe.call({
						method: "moet_careers.assessments.invite_from_desk",
						args: { job_applicant: frm.doc.name, ...values },
						callback: (r) => {
							d.hide();
							frappe.show_alert({ message: __("Invitation {0} sent", [r.message]), indicator: "green" });
							frm.reload_doc();
						},
					});
				},
			});
			d.show();
		}, __("Assessments"));
		frm.add_custom_button(__("View Invitations"), () => {
			frappe.set_route("List", "Assessment Invitation", { job_applicant: frm.doc.name });
		}, __("Assessments"));
	},
});
