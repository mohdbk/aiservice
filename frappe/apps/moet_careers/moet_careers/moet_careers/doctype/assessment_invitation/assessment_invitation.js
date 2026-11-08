frappe.ui.form.on("Assessment Invitation", {
	refresh(frm) {
		if (frm.is_new()) return;
		if (frm.doc.status !== "In Progress") {
			frm.add_custom_button(__("Allow Re-exam"), () => {
				const d = new frappe.ui.Dialog({
					title: __("Allow Re-exam"),
					fields: [
						{ fieldname: "extra_attempts", fieldtype: "Int", label: __("Extra Attempts"), default: 1 },
						{ fieldname: "deadline_days", fieldtype: "Int", label: __("New Deadline (Days)"), default: 7 },
						{ fieldname: "notify", fieldtype: "Check", label: __("Email the Candidate"), default: 1 },
					],
					primary_action_label: __("Allow Re-exam"),
					primary_action(values) {
						frappe.call({
							method: "moet_careers.assessments.allow_reexam",
							args: { invitation: frm.doc.name, ...values },
							callback: () => { d.hide(); frm.reload_doc(); },
						});
					},
				});
				d.show();
			});
		}
		if (!["Cancelled", "Marked"].includes(frm.doc.status)) {
			frm.add_custom_button(__("Cancel Invitation"), () => {
				frappe.confirm(__("Cancel this invitation? The candidate will no longer see the test."), () =>
					frappe.call({ method: "moet_careers.assessments.cancel_invitation", args: { invitation: frm.doc.name },
						callback: () => frm.reload_doc() }));
			});
		}
		if (frm.doc.last_attempt) {
			frm.add_custom_button(__("Open Last Attempt"), () => frappe.set_route("Form", "Assessment Attempt", frm.doc.last_attempt));
		}
	},
});
